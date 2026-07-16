using System.Net.Sockets;
using System.Net;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Hosting;

/// <summary>Connection lifecycle between the studio and a hosted owned app.</summary>
public enum ConnectionState
{
    Disconnected,
    Launching,
    Connected,
}

/// <summary>Whether the host owns the app's process or attached to an existing instance.</summary>
public enum HostKind
{
    None,
    Owned,
    Attached,
}

/// <summary>
/// What an <see cref="AppHost{TApp}"/> needs to start an owned app. Each app extends this with its own
/// launch parameters (e.g. the engine's launcher path and app module); the host itself reads only the
/// display name and connect timeout, handing the whole record to <see cref="OwnedApp.Launch"/>.
/// </summary>
public abstract record AppLaunchInfo
{
    /// <summary>The display name session log lines use.</summary>
    public abstract string Name { get; }

    /// <summary>How long the host waits for the launched app to accept the RPC connection.</summary>
    public int ConnectTimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// Hosts one owned app: either launching and owning its process, or attaching to an already-running
/// instance without taking it over. Ask it to <see cref="StartAsync"/>, <see cref="AttachAsync"/>,
/// <see cref="StopAsync"/>, or <see cref="RestartAsync"/>; it drives the app's connection, tears
/// everything down when either side goes away, and relaunches a crashed owned app (with backoff, using
/// the same launch info — the binaries didn't change). The app-specific steps of the lifecycle run
/// through <see cref="OwnedApp"/>'s seams (Launch/GreetAsync/RequestShutdownAsync/OnDetachingAsync);
/// everything here is generic, so the studio hosts the engine as <c>AppHost&lt;Engine&gt;</c>.
/// </summary>
public sealed class AppHost<TApp> :
    EventSubscriber,
    IAsyncDisposable,
    IEventHandler<AppDisconnected>,
    IEventHandler<AppExited>
    where TApp : OwnedApp
{
    private const int MaxRapidRestarts = 5;

    private static readonly string AppName = typeof(TApp).Name;
    private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinimumUptimeForRestart = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AttachConnectTimeout = TimeSpan.FromSeconds(5);

    // Auto-restart backoff: a reproducibly-crashing app must not loop launch→crash forever pinning the
    // CPU. Each rapid failure (one that didn't stay connected past the minimum uptime) bumps a counter
    // that both delays the next attempt (exponential, capped) and, past the cap, gives up.
    private static readonly TimeSpan RestartBackoffBase = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RestartBackoffCap = TimeSpan.FromSeconds(30);

    private readonly Logger _log;
    private readonly bool _restartOnCrash;
    private readonly object _sync = new();

    private bool _isStopping;
    private int _connectionLossHandled;

    // The launch parameters of the current/last owned app, for restart and crash auto-restart.
    private AppLaunchInfo? _lastLaunch;

    // Uptime is measured from a successful CONNECTION, not launch start, so a slow boot is never
    // mistaken for a healthy run that earns an immediate auto-restart.
    private DateTime _lastConnectedTimeUtc = DateTime.MinValue;

    // Counts consecutive rapid restart failures (reset on a connection that lasts past the minimum
    // uptime), driving the auto-restart backoff and give-up cap.
    private int _consecutiveRapidFailures;

    // Host-lifetime cancellation: cancelled by StopAsync/DisposeAsync so an in-flight connect (or a
    // pending auto-restart backoff) is torn down instead of finishing into a stopped host.
    private CancellationTokenSource _lifetimeCts = new();

    public AppHost(TApp app, Logger log, EventDispatcher events, bool restartOnCrash = true)
        : base(events)
    {
        App = app;
        _log = log;
        _restartOnCrash = restartOnCrash;
    }

    /// <summary>The hosted app itself — the service consumers talk to (commands, state, input).</summary>
    public TApp App { get; }

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    // Owned vs attached; travels on every ConnectionChanged so consumers never need to ask the host.
    private HostKind Kind { get; set; } = HostKind.None;

    /// <summary>A consistent snapshot of the current host-lifetime token (the field can be swapped).</summary>
    private CancellationToken LifetimeToken
    {
        get
        {
            lock (_sync)
            {
                return _lifetimeCts.Token;
            }
        }
    }

    /// <summary>A dropped connection funnels into the shared loss handler (it races process exit).</summary>
    public void Handle(in AppDisconnected evt) => TryHandleConnectionLoss();

    public void Handle(in AppExited evt)
    {
        _log.Log(
            evt.ExitCode == 0 ? LogLevel.Info : LogLevel.Warning,
            $"{AppName} process exited (code {evt.ExitCode}).");
        TryHandleConnectionLoss();
    }

    /// <summary>
    /// Launches and owns an app process from the given launch info. Failures are reported as studio log
    /// lines. A no-op while a session is already starting or live.
    /// </summary>
    public async Task StartAsync(AppLaunchInfo launch, CancellationToken ct)
    {
        // Reserves the host atomically against a racing attach.
        if (!TryBeginConnecting(HostKind.Owned))
            return;

        _lastLaunch = launch;
        _log.Info($"=== Session: {launch.Name} ===");
        Interlocked.Exchange(ref _connectionLossHandled, 0);

        // Link the caller's token to the host lifetime so teardown (StopAsync/DisposeAsync) cancels an
        // in-progress connect even when the caller passed an uncancellable token. A fresh launch starts
        // a fresh lifetime, so the previous (possibly cancelled) token must be renewed first.
        var lifetimeToken = RenewLifetime();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetimeToken);
        ct = linked.Token;
        try
        {
            var port = GetFreeLoopbackPort();
            _log.Info($"Launching '{launch.Name}' on RPC port {port}...");

            App.Launch(launch, port);
            _log.Info($"{AppName} process started (pid {App.ProcessId}).");

            if (!await ConnectAndGreetAsync(
                    port, TimeSpan.FromSeconds(launch.ConnectTimeoutSeconds), ct).ContinueOnAnyContext())
            {
                await TearDownAsync(killProcess: true).ContinueOnAnyContext();
            }
        }
        catch (Exception exception)
        {
            _log.Error($"Launch failed: {exception.Message}");
            await TearDownAsync(killProcess: true).ContinueOnAnyContext();
        }
    }

    /// <summary>
    /// Attaches to an app that is already running. The instance is never taken over: stopping merely
    /// detaches. A no-op while a session is already starting or live.
    /// </summary>
    public async Task AttachAsync(int port)
    {
        if (!TryBeginConnecting(HostKind.Attached))
            return;

        Interlocked.Exchange(ref _connectionLossHandled, 0);
        // A fresh attach starts a fresh lifetime so teardown can cancel the connect handshake.
        var lifetimeToken = RenewLifetime();
        try
        {
            _log.Info($"Attaching to running {AppName} on :{port}...");
            if (!await ConnectAndGreetAsync(port, AttachConnectTimeout, lifetimeToken).ContinueOnAnyContext())
            {
                await TearDownAsync(killProcess: false).ContinueOnAnyContext();
                return;
            }

            _log.Info($"Attached to running {AppName} on :{port}.");
        }
        catch (Exception exception)
        {
            _log.Error($"Attach failed: {exception.Message}");
            await TearDownAsync(killProcess: false).ContinueOnAnyContext();
        }
    }

    /// <summary>
    /// Stops the session. An owned app is asked to exit gracefully before being killed; an attached app
    /// is simply detached from and keeps running.
    /// </summary>
    public async Task StopAsync()
    {
        lock (_sync)
        {
            if (State == ConnectionState.Disconnected || _isStopping)
                return;

            _isStopping = true;
        }

        // Abort any in-flight connect (or pending auto-restart) tied to this lifetime so a teardown
        // mid-launch doesn't finish into a stopped host. A following relaunch renews the token.
        CancelLifetime();

        try
        {
            if (Kind == HostKind.Attached)
            {
                _log.Info($"Detaching from {AppName}; it keeps running.");
                await App.OnDetachingAsync().ContinueOnAnyContext();
                await TearDownAsync(killProcess: false).ContinueOnAnyContext();
                return;
            }

            if (App.IsConnected && App.IsRunning)
            {
                _log.Info($"Requesting {AppName} shutdown...");
                await App.RequestShutdownAsync().ContinueOnAnyContext();

                if (!await App.WaitForExitAsync(GracefulExitTimeout).ContinueOnAnyContext())
                    _log.Warning($"{AppName} did not exit in time; killing the process.");
            }

            await TearDownAsync(killProcess: true).ContinueOnAnyContext();
        }
        finally
        {
            lock (_sync)
            {
                _isStopping = false;
            }
        }
    }

    /// <summary>
    /// Stops whatever is running and relaunches the last launch info (the same binaries); a no-op when
    /// nothing has been launched yet.
    /// </summary>
    public async Task RestartAsync()
    {
        if (_lastLaunch is not { } launch)
            return;

        await StopAsync().ContinueOnAnyContext();
        await StartAsync(launch, CancellationToken.None).ContinueOnAnyContext();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose(); // Unregisters the host's event handlers (the EventSubscriber base).
        await StopAsync().ContinueOnAnyContext();
        lock (_sync)
        {
            _lifetimeCts.Dispose();
        }
    }

    /// <summary>
    /// Connects the app's transport and runs its greet handshake, completing the session on success.
    /// False (with the error logged) on either failure; the caller owns teardown.
    /// </summary>
    private async Task<bool> ConnectAndGreetAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        var connected = await App.ConnectAsync(port, timeout, ct).ContinueOnAnyContext();
        if (!connected.Success)
        {
            _log.Error($"Connect failed: {connected.Error}");
            return false;
        }

        var greeted = await App.GreetAsync(ct).ContinueOnAnyContext();
        if (!greeted.Success)
        {
            _log.Error($"Handshake failed: {greeted.Error}");
            return false;
        }

        // Uptime for the auto-restart guard is measured from here (a real connection), not launch start.
        _lastConnectedTimeUtc = DateTime.UtcNow;
        SetState(ConnectionState.Connected);
        return true;
    }

    /// <summary>
    /// Funnels both loss signals (RPC disconnect and process exit) into one handler; on a crash they
    /// race and whichever lands first owns teardown and the restart decision.
    /// </summary>
    private void TryHandleConnectionLoss()
    {
        lock (_sync)
        {
            if (_isStopping)
                return;
        }

        if (Interlocked.Exchange(ref _connectionLossHandled, 1) == 1)
            return;

        HandleConnectionLossAsync().FireAndForget();
    }

    private async Task HandleConnectionLossAsync()
    {
        var wasOwned = Kind == HostKind.Owned;

        // Did the app stay connected long enough to count as a healthy run? Measured from the successful
        // connection, so a slow boot can never masquerade as uptime. A connection that never completed
        // leaves _lastConnectedTimeUtc at MinValue, so this is correctly false.
        var stayedHealthy = _lastConnectedTimeUtc != DateTime.MinValue
            && DateTime.UtcNow - _lastConnectedTimeUtc > MinimumUptimeForRestart;

        await TearDownAsync(killProcess: wasOwned).ContinueOnAnyContext();

        if (!wasOwned)
        {
            _log.Info($"Connection to the attached {AppName} was lost.");
            return;
        }

        if (!_restartOnCrash || _lastLaunch is not { } launch)
            return;

        // A run that lasted past the minimum uptime is treated as a clean crash: reset the rapid-failure
        // streak so a long-lived app that finally crashes restarts immediately. A run that died quickly
        // bumps the streak, backing off (and eventually giving up) to avoid a CPU-pinning restart loop.
        if (stayedHealthy)
        {
            _consecutiveRapidFailures = 0;
        }
        else if (++_consecutiveRapidFailures > MaxRapidRestarts)
        {
            _log.Error(
                $"{AppName} crashed {MaxRapidRestarts} times in quick succession; auto-restart has "
                    + "given up. Relaunch manually.");
            return;
        }

        var lifetimeToken = LifetimeToken;
        if (lifetimeToken.IsCancellationRequested)
            return;

        var backoff = RestartBackoff(_consecutiveRapidFailures);
        if (backoff > TimeSpan.Zero)
        {
            _log.Warning(
                $"{AppName} connection lost; restarting in {backoff.TotalSeconds:F0}s "
                    + $"(attempt {_consecutiveRapidFailures} of {MaxRapidRestarts})...");
            try
            {
                await Task.Delay(backoff, lifetimeToken).ContinueOnAnyContext();
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
        else
        {
            _log.Warning($"{AppName} connection lost; restarting it...");
        }

        // Relaunch the same binaries — a crash didn't change what the launch info points at.
        await StartAsync(launch, CancellationToken.None).ContinueOnAnyContext();
    }

    /// <summary>
    /// The exponential, capped backoff before the Nth consecutive rapid restart. The first failure
    /// restarts immediately (zero delay) so a one-off crash recovers instantly; later failures back off.
    /// </summary>
    private static TimeSpan RestartBackoff(int rapidFailures)
    {
        if (rapidFailures <= 1)
            return TimeSpan.Zero;

        var seconds = RestartBackoffBase.TotalSeconds * Math.Pow(2, rapidFailures - 2);
        return TimeSpan.FromSeconds(Math.Min(seconds, RestartBackoffCap.TotalSeconds));
    }

    /// <summary>
    /// Cancels any in-flight connect or pending auto-restart tied to the current host lifetime.
    /// The next launch renews the token.
    /// </summary>
    private void CancelLifetime()
    {
        CancellationTokenSource cts;
        lock (_sync)
        {
            cts = _lifetimeCts;
        }

        cts.Cancel();
    }

    /// <summary>
    /// Swaps in a fresh host-lifetime cancellation source for a new launch (disposing the spent one) and
    /// returns its token. A new owned/attached session is a new lifetime, so a prior cancel must not stick.
    /// </summary>
    private CancellationToken RenewLifetime()
    {
        CancellationTokenSource fresh = new();
        CancellationTokenSource old;
        lock (_sync)
        {
            old = _lifetimeCts;
            _lifetimeCts = fresh;
        }

        old.Dispose();
        return fresh.Token;
    }

    private async Task TearDownAsync(bool killProcess)
    {
        lock (_sync)
        {
            if (State == ConnectionState.Disconnected && !App.IsRunning && !App.IsConnected)
                return;
        }

        // We own this disconnect, so flag the loss as handled before tearing the connection down: the
        // app's disconnected event (fired by Disconnect below) must not be mistaken for a crash. The
        // owned process's exited event (fired by the kill below) is guarded by the same flag.
        Interlocked.Exchange(ref _connectionLossHandled, 1);
        App.Disconnect();

        if (killProcess && App.IsRunning)
            _log.Warning($"Killing {AppName} process.");
        // Release the owned process handle: when killing, wait for it to actually exit — a lingering
        // handle can hold the app's binaries locked (the engine's are built in-tree, so the next
        // compile's relink would fail with a permission-denied error). A no-op for an attached session.
        await App.StopProcessAsync(killProcess, GracefulExitTimeout).ContinueOnAnyContext();

        Kind = HostKind.None;
        SetState(ConnectionState.Disconnected);
    }

    /// <summary>
    /// Atomically reserves the Disconnected→Launching transition (as <paramref name="kind"/>) so a start
    /// and an auto-attach (raised on the detector's background thread) can't both enter a session.
    /// Returns false if a session is already starting or live; the caller should bail.
    /// </summary>
    private bool TryBeginConnecting(HostKind kind)
    {
        lock (_sync)
        {
            if (State != ConnectionState.Disconnected)
                return false;

            State = ConnectionState.Launching;
            Kind = kind;
        }

        Events.Dispatch(new ConnectionChanged(ConnectionState.Launching, kind));
        return true;
    }

    private void SetState(ConnectionState state)
    {
        HostKind kind;
        lock (_sync)
        {
            if (State == state)
                return;

            State = state;
            kind = Kind;
        }

        Events.Dispatch(new ConnectionChanged(state, kind));
    }

    private static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
