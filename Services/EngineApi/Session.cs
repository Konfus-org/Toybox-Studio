using Toybox.Studio.Utils;
using System.Net;
using System.Net.Sockets;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Services.Settings;

namespace Toybox.Studio.Services.EngineApi;

/// <summary>
/// Whether the current session owns the engine process or attached to an existing one.
/// </summary>
public enum SessionKind
{
    None,
    Owned,
    Attached,
}

/// <summary>
/// Coordinates one engine session — either launching and owning an engine process, or attaching
/// to an already-running instance without taking it over. Drives the shared <see cref="EngineRpc"/>'s
/// connection, keeps a ping loop alive, and tears everything down when either side goes away.
/// </summary>
public sealed class Session : IAsyncDisposable
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinimumUptimeForRestart = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AttachConnectTimeout = TimeSpan.FromSeconds(5);

    // Auto-restart backoff: a reproducibly-crashing engine must not loop compile→launch→crash forever
    // pinning the CPU. Each rapid failure (one that didn't stay connected past the minimum uptime) bumps
    // a counter that both delays the next attempt (exponential, capped) and, past the cap, gives up.
    private static readonly TimeSpan RestartBackoffBase = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RestartBackoffCap = TimeSpan.FromSeconds(30);
    private const int MaxRapidRestarts = 5;

    // Each watchdog ping is bounded so a frozen engine can't park the probe forever; if no ping
    // succeeds within this window the engine is treated as unresponsive (so the editor can offer to
    // force-restart it). A short retry pause keeps a failing probe from spinning.
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PingRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan UnresponsiveThreshold = TimeSpan.FromSeconds(5);

    private readonly EditorSettings _settings;
    private readonly ProjectManager _projects;
    private readonly ProjectBuilder _builder;
    private readonly Logger _log;
    private readonly EngineRpc _engine;
    private readonly object _sync = new();

    private Engine? _process;
    private CancellationTokenSource? _watchdogCts;
    private bool _isStopping;
    private int _connectionLossHandled;

    // Uptime is measured from a successful CONNECTION, not launch start: a slow compile (which can exceed
    // the minimum-uptime window) must not be mistaken for a healthy run that earns an auto-restart.
    private DateTime _lastConnectedTimeUtc = DateTime.MinValue;

    // Counts consecutive rapid restart failures (reset on a connection that lasts past the minimum uptime),
    // driving the auto-restart backoff and give-up cap.
    private int _consecutiveRapidFailures;

    // Session-lifetime cancellation: cancelled by StopAsync/DisposeAsync so an in-flight compile/launch
    // (including an auto-restart or relaunch) is torn down instead of orphaning CMake/Ninja.
    private CancellationTokenSource _lifetimeCts = new();

    // Serializes launch/attach/restart transitions so an auto-attach, a project-change restart, and a
    // crash auto-restart can't interleave while State briefly sits at Disconnected between stop and launch.
    private readonly SemaphoreSlim _transitionGate = new(1, 1);

    // Coalesces rapid project changes: only the latest pending project switch is honoured.
    private int _pendingProjectChange;

    // Watchdog state, shared between the watchdog loop and the UI (Snooze): the tick of the last
    // successful ping, and whether the engine is currently flagged unresponsive (0/1 for Interlocked).
    private long _lastPingOkTicks;
    private int _isUnresponsive;

    public Session(
        SettingsManager settings,
        ProjectManager projects,
        ProjectBuilder builder,
        Logger log,
        EngineRpc engine)
    {
        _settings = settings.Settings;
        _projects = projects;
        _builder = builder;
        _log = log;
        _engine = engine;
        // The engine is a stable singleton, so wire its streams once: log lines flow into the unified log,
        // and a dropped connection funnels into the same loss handler as a process exit.
        _engine.LogReceived += entry => _log.Log(entry.Level, entry.Message);
        _engine.Disconnected += OnEngineDisconnected;
        // The build runs outside the session; surface its "Compiling" phase through the session's busy state
        // so the rest of the editor still sees one signal for "engine work in progress".
        _builder.BuildingChanged += OnBuildingChanged;
        // The engine now runs continuously in editor mode, one per open project: switching projects
        // relaunches it so its world matches.
        _projects.ProjectChanged += OnProjectChanged;
    }

    public event Action<ConnectionState>? StateChanged;

    public event Action<TimeSpan>? PingMeasured;

    /// <summary>
    /// Raised (off the UI thread) when the engine has not answered a ping for longer than the
    /// unresponsive threshold while still connected — i.e. it appears frozen. Subscribers should
    /// marshal to the UI themselves. The editor uses this to offer a force-restart.
    /// </summary>
    public event Action? Unresponsive;

    /// <summary>
    /// Raised (off the UI thread) when a previously unresponsive engine answers again, or the session
    /// is torn down — a signal to dismiss any "not responding" prompt.
    /// </summary>
    public event Action? Responsive;

    /// <summary>
    /// Raised when launch/compile work starts or finishes (drives loading UI).
    /// </summary>
    public event Action<bool>? BusyChanged;

    /// <summary>
    /// Raised when a project compile starts (true) or finishes (false). Lets the watcher tell the
    /// "Compiling" phase apart from the rest of a launch (both of which keep <see cref="IsBusy"/> set).
    /// </summary>
    public event Action<bool>? CompilingChanged;

    public event Action<bool>? PausedChanged;

    /// <summary>Raised when play mode is entered or exited (drives the transport's play/stop state).</summary>
    public event Action<bool>? PlayingChanged;

    /// <summary>
    /// Raised the instant a play request begins, before the engine round-trip — so the watcher can show
    /// the game-loading phase during the (brief) transition. <see cref="PlayingChanged"/> ends it.
    /// </summary>
    public event Action? PlayStarting;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public SessionKind Kind { get; private set; } = SessionKind.None;

    public bool IsBusy { get; private set; }

    public bool IsPaused { get; private set; }

    /// <summary>Whether the engine is currently in play mode (running the game loop), vs editor mode.</summary>
    public bool IsPlaying { get; private set; }

    public string? ConnectedAppName { get; private set; }

    /// <summary>
    /// Compiles, launches, and owns the current project's engine process. Failures are reported
    /// as studio log lines.
    /// </summary>
    public async Task LaunchAsync(CancellationToken ct)
    {
        if (State != ConnectionState.Disconnected)
            return;

        var project = _projects.CurrentProject;
        if (project is null)
        {
            _log.Error("Open a project to launch.");
            return;
        }

        // The check above is a fast path; this reserves the session atomically against a racing attach.
        if (!TryBeginConnecting())
            return;

        Kind = SessionKind.Owned;
        _log.Info($"=== Session: {project.Name} ===");
        Interlocked.Exchange(ref _connectionLossHandled, 0);

        // Link the caller's token to the session lifetime so teardown (StopAsync/DisposeAsync) cancels an
        // in-progress compile/connect even when the caller passed an uncancellable token. A fresh launch
        // starts a fresh lifetime, so the previous (possibly cancelled) token must be renewed first.
        var lifetimeToken = RenewLifetime();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetimeToken);
        ct = linked.Token;
        try
        {
            if (!await _builder.BuildAsync(ct).ContinueOnAnyContext())
            {
                await TearDownAsync(killProcess: false).ContinueOnAnyContext();
                return;
            }

            var launcherPath = ProjectBuilder.FindProjectLauncher(
                project.BuildDirectory, ProjectBuilder.BuildConfiguration);
            if (launcherPath is null)
            {
                _log.Error("The project build did not produce a Launcher executable.");
                await TearDownAsync(killProcess: false).ContinueOnAnyContext();
                return;
            }

            var port = GetFreeLoopbackPort();
            _log.Info($"Launching project '{project.Name}' on RPC port {port}...");

            // The bundled asset-viewer content (preview sky + base world) lives beside the editor and
            // is handed to the engine as an extra asset root so previews can load it for any project.
            var assetViewerDir = Path.Combine(AppContext.BaseDirectory, "Assets", "AssetViewer");

            _process = Engine.Start(
                launcherPath,
                project.ModuleName,
                project.AppSettingsPath,
                _settings.Engine.HideEngineWindow,
                port,
                assetViewerDir);
            _process.Exited += OnProcessExited;
            _log.Info($"Engine process started (pid {_process.Id}).");

            var connect = await _engine.ConnectAsync(
                port,
                TimeSpan.FromSeconds(_settings.Engine.ConnectTimeoutSeconds),
                ct).ContinueOnAnyContext();
            if (connect is not { Success: true, Value: { } hello })
            {
                _log.Error($"Launch failed: {connect.Error}");
                await TearDownAsync(killProcess: true).ContinueOnAnyContext();
                return;
            }

            CompleteConnection(hello);
        }
        catch (Exception exception)
        {
            _log.Error($"Launch failed: {exception.Message}");
            await TearDownAsync(killProcess: true).ContinueOnAnyContext();
        }
    }

    /// <summary>
    /// Attaches to an engine that is already running. The instance is never taken over: the
    /// session only reads data and injects the editor view, and stopping merely detaches.
    /// </summary>
    public async Task AttachAsync(int port)
    {
        if (!TryBeginConnecting())
            return;

        Kind = SessionKind.Attached;
        Interlocked.Exchange(ref _connectionLossHandled, 0);
        // A fresh attach starts a fresh lifetime so teardown can cancel the connect handshake.
        var lifetimeToken = RenewLifetime();
        try
        {
            _log.Info($"Attaching to running engine on :{port}...");
            var connect = await _engine.ConnectAsync(port, AttachConnectTimeout, lifetimeToken)
                .ContinueOnAnyContext();
            if (connect is not { Success: true, Value: { } hello })
            {
                _log.Error($"Attach failed: {connect.Error}");
                await TearDownAsync(killProcess: false).ContinueOnAnyContext();
                return;
            }

            CompleteConnection(hello);
            _log.Info($"Attached to running engine on :{port}.");
        }
        catch (Exception exception)
        {
            _log.Error($"Attach failed: {exception.Message}");
            await TearDownAsync(killProcess: false).ContinueOnAnyContext();
        }
    }

    /// <summary>
    /// Stops the session. Owned engines are asked to exit gracefully before being killed;
    /// attached engines are simply detached from and keep running.
    /// </summary>
    public async Task StopAsync()
    {
        lock (_sync)
        {
            if (State == ConnectionState.Disconnected || _isStopping)
                return;

            _isStopping = true;
        }

        // Abort any in-flight compile/connect tied to this lifetime so a teardown mid-launch doesn't leave
        // CMake/Ninja running. A following relaunch renews the token, so this is safe across a restart.
        CancelLifetime();

        try
        {
            if (Kind == SessionKind.Attached)
            {
                _log.Info("Detaching from engine; it keeps running.");
                if (IsPaused)
                    await SetPausedAsync(false).ContinueOnAnyContext();

                await TearDownAsync(killProcess: false).ContinueOnAnyContext();
                return;
            }

            var process = _process;
            if (_engine.IsConnected && process is not null)
            {
                _log.Info("Requesting engine shutdown...");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                // Best-effort: the engine may drop the connection before replying, so the failure (if any)
                // is ignored — the process wait below decides whether a kill is still needed.
                await _engine.ShutdownAsync(cts.Token).ContinueOnAnyContext();

                if (!await process.WaitForExitAsync(GracefulExitTimeout).ContinueOnAnyContext())
                    _log.Warning("Engine did not exit in time; killing the process.");
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

    public async ValueTask DisposeAsync()
    {
        _projects.ProjectChanged -= OnProjectChanged;
        _engine.Disconnected -= OnEngineDisconnected;
        _builder.BuildingChanged -= OnBuildingChanged;
        await StopAsync().ContinueOnAnyContext();
        _transitionGate.Dispose();
        lock (_sync)
        {
            _lifetimeCts.Dispose();
        }
    }

    /// <summary>
    /// The user chose to keep waiting on a frozen engine: treat now as the last good moment so the
    /// watchdog re-arms and prompts again only if the engine stays frozen for another full threshold.
    /// </summary>
    public void SnoozeWatchdog()
    {
        Interlocked.Exchange(ref _lastPingOkTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _isUnresponsive, 0);
    }

    /// <summary>
    /// Pauses or resumes the connected engine's simulation.
    /// </summary>
    public async Task SetPausedAsync(bool isPaused)
    {
        if (!_engine.IsConnected)
            return;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await _engine.SetPausedAsync(isPaused, cts.Token).ContinueOnAnyContext();
        if (!result.Success)
        {
            _log.Error($"Pause request failed: {result.Error}");
            return;
        }

        IsPaused = isPaused;
        PausedChanged?.Invoke(isPaused);
        _log.Info(isPaused ? "Engine paused." : "Engine resumed.");
    }

    /// <summary>
    /// Enters play mode: the engine snapshots its world and starts running the game loop. The engine
    /// itself keeps running, so the viewports stay live.
    /// </summary>
    public Task StartPlayAsync() => SetPlayingAsync(true);

    /// <summary>
    /// Exits play mode: the engine restores the pre-play world and stops simulating, without stopping
    /// the engine or the viewports.
    /// </summary>
    public Task StopPlayAsync() => SetPlayingAsync(false);

    /// <summary>
    /// Stops and relaunches the current project's engine, recompiling as part of launch (a compiler change
    /// clean-reconfigures inside <see cref="ProjectBuilder.BuildAsync(CancellationToken)"/>). Used when an editor
    /// setting that changes the native build — the C++ compiler or the engine source path — was edited and
    /// confirmed. No-op when nothing is running; the change applies on the next launch.
    /// </summary>
    public async Task RebuildAndRelaunchAsync()
    {
        if (State == ConnectionState.Disconnected)
            return;

        await RestartForProjectAsync().ContinueOnAnyContext();
    }

    /// <summary>
    /// Force-restarts a frozen engine: hard-kills the process (skipping the graceful <c>engine.shutdown</c>
    /// handshake a frozen engine would never answer) and relaunches it. Unlike crash auto-restart, this is
    /// an explicit user action, so it ignores the <c>RestartOnCrash</c> setting and the minimum-uptime guard.
    /// An attached engine isn't owned, so it can only be detached from, not killed or relaunched.
    /// </summary>
    public async Task ForceRestartAsync()
    {
        lock (_sync)
        {
            if (State == ConnectionState.Disconnected || _isStopping)
                return;

            _isStopping = true;
        }

        var wasOwned = Kind == SessionKind.Owned;
        try
        {
            await TearDownAsync(killProcess: wasOwned).ContinueOnAnyContext();
        }
        finally
        {
            lock (_sync)
            {
                _isStopping = false;
            }
        }

        if (wasOwned)
            await LaunchSerializedAsync().ContinueOnAnyContext();
        else
            _log.Info("Detached from the unresponsive engine; it was not owned, so it keeps running.");
    }

    /// <summary>
    /// Launches under the transition gate and with the session-lifetime token, so a launch can't interleave
    /// with a concurrent attach/restart and is cancelled by teardown. Used by every internal relaunch path.
    /// </summary>
    private async Task LaunchSerializedAsync()
    {
        await _transitionGate.WaitAsync().ContinueOnAnyContext();
        try
        {
            // LaunchAsync renews and links the session-lifetime token itself, so None here is correct.
            await LaunchAsync(CancellationToken.None).ContinueOnAnyContext();
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    private void CompleteConnection(Hello hello)
    {
        ConnectedAppName = hello.App;
        // Uptime for the auto-restart guard is measured from here (a real connection), not launch start.
        _lastConnectedTimeUtc = DateTime.UtcNow;
        SetState(ConnectionState.Connected);
        // From now on, studio log lines also flow into the engine's unified log, and the engine
        // console's colors track the editor theme.
        _log.SetEngineForwarder(_engine.WriteLogAsync);
        _log.SetLogColorSink(_engine.SetLogColorsAsync);
        _log.Info(
            $"Connected to {hello.Engine} (app '{hello.App}', protocol v{hello.ProtocolVersion}).");

        Interlocked.Exchange(ref _isUnresponsive, 0);
        Interlocked.Exchange(ref _lastPingOkTicks, DateTime.UtcNow.Ticks);
        _watchdogCts = new CancellationTokenSource();
        RunWatchdogAsync(_watchdogCts.Token).FireAndForget();
    }

    /// <summary>
    /// Keeps the connection honest: probes the engine with a bounded ping on a cadence, reports the
    /// round-trip for the status bar, and — crucially — notices when a still-connected engine stops
    /// answering. A frozen engine keeps its socket open, so neither a process exit nor an RPC disconnect
    /// fires; this loop is the only thing that catches that case and raises <see cref="Unresponsive"/>.
    /// </summary>
    private async Task RunWatchdogAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Bound each probe so a frozen engine parks this attempt, not the whole loop.
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(PingTimeout);

                var ping = await _engine.PingAsync(attempt.Token).ContinueOnAnyContext();
                if (ct.IsCancellationRequested)
                    break;

                if (ping.Success)
                {
                    Interlocked.Exchange(ref _lastPingOkTicks, DateTime.UtcNow.Ticks);
                    PingMeasured?.Invoke(ping.Value);
                    MarkResponsive();
                    // Healthy: idle for the normal interval before the next probe.
                    await Task.Delay(PingInterval, ct).ContinueOnAnyContext();
                }
                else
                {
                    // No reply (timed out or errored) while still connected: the engine may be frozen.
                    // Keep probing without the idle interval so we cross the threshold promptly.
                    EvaluateResponsiveness();
                    await Task.Delay(PingRetryDelay, ct).ContinueOnAnyContext();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled on teardown.
        }
        catch (Exception)
        {
            // The connection died; the Disconnected handler owns cleanup.
        }
    }

    /// <summary>
    /// Flags the engine unresponsive (once) when it has gone past the threshold without a good ping,
    /// raising <see cref="Unresponsive"/>. Only meaningful while we still believe we're connected.
    /// </summary>
    private void EvaluateResponsiveness()
    {
        if (State != ConnectionState.Connected)
            return;

        var sinceLastOk = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastPingOkTicks));
        if (sinceLastOk < UnresponsiveThreshold)
            return;

        if (Interlocked.Exchange(ref _isUnresponsive, 1) == 1)
            return;

        _log.Warning(
            $"Engine has not responded for {sinceLastOk.TotalSeconds:F0}s; it may be frozen.");
        Unresponsive?.Invoke();
    }

    /// <summary>Clears the unresponsive flag and announces recovery, if it was set.</summary>
    private void MarkResponsive()
    {
        if (Interlocked.Exchange(ref _isUnresponsive, 0) == 0)
            return;

        _log.Info("Engine resumed responding.");
        Responsive?.Invoke();
    }

    private void OnProcessExited(int exitCode)
    {
        _log.Log(
            exitCode == 0 ? LogLevel.Info : LogLevel.Warning,
            $"Engine process exited (code {exitCode}).");
        TryHandleConnectionLoss();
    }

    private void OnEngineDisconnected()
    {
        TryHandleConnectionLoss();
    }

    /// <summary>
    /// Funnels both loss signals (RPC disconnect and process exit) into one handler; on a crash
    /// they race and whichever lands first owns teardown and the restart decision.
    /// </summary>
    private void TryHandleConnectionLoss()
    {
        if (IsStopInProgress())
            return;

        if (Interlocked.Exchange(ref _connectionLossHandled, 1) == 1)
            return;

        HandleConnectionLossAsync().FireAndForget();
    }

    private async Task HandleConnectionLossAsync()
    {
        var wasOwned = Kind == SessionKind.Owned;

        // Did the engine stay connected long enough to count as a healthy run? Measured from the successful
        // connection, so a slow compile can never masquerade as uptime. A connection that never completed
        // leaves _lastConnectedTimeUtc at MinValue, so this is correctly false.
        var stayedHealthy = _lastConnectedTimeUtc != DateTime.MinValue
            && DateTime.UtcNow - _lastConnectedTimeUtc > MinimumUptimeForRestart;

        await TearDownAsync(killProcess: wasOwned).ContinueOnAnyContext();

        if (!wasOwned)
        {
            _log.Info("Connection to the attached engine was lost.");
            return;
        }

        if (!_settings.Engine.RestartOnCrash)
            return;

        // A run that lasted past the minimum uptime is treated as a clean crash: reset the rapid-failure
        // streak so a long-lived engine that finally crashes restarts immediately. A run that died quickly
        // bumps the streak, backing off (and eventually giving up) to avoid a CPU-pinning restart loop.
        if (stayedHealthy)
        {
            _consecutiveRapidFailures = 0;
        }
        else if (++_consecutiveRapidFailures > MaxRapidRestarts)
        {
            _log.Error(
                $"Engine crashed {MaxRapidRestarts} times in quick succession; auto-restart has given up. "
                    + "Fix the project and relaunch manually.");
            return;
        }

        var lifetimeToken = LifetimeToken;
        if (lifetimeToken.IsCancellationRequested)
            return;

        var backoff = RestartBackoff(_consecutiveRapidFailures);
        if (backoff > TimeSpan.Zero)
        {
            _log.Warning(
                $"Engine connection lost; restarting in {backoff.TotalSeconds:F0}s "
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
            _log.Warning("Engine connection lost; restarting the engine...");
        }

        await LaunchSerializedAsync().ContinueOnAnyContext();
    }

    /// <summary>
    /// The exponential, capped backoff before the Nth consecutive rapid restart. The first failure restarts
    /// immediately (zero delay) so a one-off crash recovers instantly; later failures back off.
    /// </summary>
    private static TimeSpan RestartBackoff(int rapidFailures)
    {
        if (rapidFailures <= 1)
            return TimeSpan.Zero;

        var seconds = RestartBackoffBase.TotalSeconds * Math.Pow(2, rapidFailures - 2);
        return TimeSpan.FromSeconds(Math.Min(seconds, RestartBackoffCap.TotalSeconds));
    }

    private bool IsStopInProgress()
    {
        lock (_sync)
        {
            return _isStopping;
        }
    }

    /// <summary>A consistent snapshot of the current session-lifetime token (the field can be swapped).</summary>
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

    /// <summary>
    /// Cancels any in-flight compile/launch tied to the current session lifetime (so a teardown stops an
    /// in-progress CMake/Ninja build instead of orphaning it). The next launch renews the token.
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
    /// Swaps in a fresh session-lifetime cancellation source for a new launch (disposing the spent one) and
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
        Engine? process;
        CancellationTokenSource? watchdogCts;
        lock (_sync)
        {
            if (State == ConnectionState.Disconnected && _process is null && !_engine.IsConnected)
                return;

            process = _process;
            watchdogCts = _watchdogCts;
            _process = null;
            _watchdogCts = null;
        }

        // We own this disconnect, so flag the loss as handled before tearing the connection down: the
        // engine's Disconnected event (fired by Disconnect below) must not be mistaken for a crash.
        Interlocked.Exchange(ref _connectionLossHandled, 1);
        // The engine is going away, so any "not responding" prompt should be dismissed (teardown owns the
        // messaging from here). Raise Responsive directly rather than via MarkResponsive to avoid the
        // misleading "resumed responding" log line during a kill.
        if (Interlocked.Exchange(ref _isUnresponsive, 0) == 1)
            Responsive?.Invoke();
        watchdogCts?.Cancel();
        watchdogCts?.Dispose();
        _engine.Disconnect();

        if (process is not null)
        {
            process.Exited -= OnProcessExited;
            if (killProcess && process.IsRunning)
            {
                _log.Warning("Killing engine process.");
                // Wait for it to actually exit: the engine binaries are built in-tree, so a lingering
                // handle would make the next compile's relink fail with a permission-denied error.
                await process.KillAndWaitAsync(GracefulExitTimeout).ContinueOnAnyContext();
            }

            process.Dispose();
        }

        _log.SetEngineForwarder(null);
        _log.SetLogColorSink(null);
        ConnectedAppName = null;
        Kind = SessionKind.None;
        if (IsPaused)
        {
            IsPaused = false;
            PausedChanged?.Invoke(false);
        }

        if (IsPlaying)
        {
            IsPlaying = false;
            PlayingChanged?.Invoke(false);
        }

        SetState(ConnectionState.Disconnected);
    }

    private async Task SetPlayingAsync(bool isPlaying)
    {
        if (!_engine.IsConnected || IsPlaying == isPlaying)
            return;

        // Surface the game-loading phase before the round-trip so the watcher can show it immediately.
        if (isPlaying)
            PlayStarting?.Invoke();

        // Leaving play clears any pause so the next play session starts clean.
        if (!isPlaying && IsPaused)
            await SetPausedAsync(false).ContinueOnAnyContext();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var result = await _engine.SetPlayingAsync(isPlaying, cts.Token).ContinueOnAnyContext();
        if (!result.Success)
        {
            _log.Error($"{(isPlaying ? "Play" : "Stop")} request failed: {result.Error}");
            // Re-announce the unchanged play state so a watcher that entered the game-loading phase on
            // PlayStarting falls back out of it instead of waiting forever for a transition that failed.
            if (isPlaying)
                PlayingChanged?.Invoke(IsPlaying);
            return;
        }

        IsPlaying = isPlaying;
        PlayingChanged?.Invoke(isPlaying);
        _log.Info(isPlaying ? "Game started." : "Game stopped.");
    }

    // A project switch relaunches the engine in editor mode so its world matches the new project. The
    // very first launch at startup is driven by App startup; this only reacts to later changes while an
    // engine is already live.
    private void OnProjectChanged(ProjectInfo? project)
    {
        if (project is null || State == ConnectionState.Disconnected)
            return;

        // Coalesce rapid project changes: if a restart is already pending/running, just flag that another
        // change arrived so the in-flight restart re-runs once with the latest project, instead of queuing
        // overlapping stop/launch sequences that fight over the session.
        if (Interlocked.Exchange(ref _pendingProjectChange, 1) == 1)
            return;

        RestartForProjectLoopAsync().FireAndForget();
    }

    // Drains coalesced project changes: restarts once per "latch", re-running while another change landed
    // during the restart so the engine always ends up on the most recently selected project.
    private async Task RestartForProjectLoopAsync()
    {
        while (Interlocked.Exchange(ref _pendingProjectChange, 0) == 1)
            await RestartForProjectAsync().ContinueOnAnyContext();
    }

    private async Task RestartForProjectAsync()
    {
        // Hold the transition gate across the whole stop→launch so a concurrent auto-attach or crash
        // auto-restart can't slip a launch in while State momentarily sits at Disconnected between them.
        await _transitionGate.WaitAsync().ContinueOnAnyContext();
        try
        {
            await StopAsync().ContinueOnAnyContext();
            // LaunchAsync renews and links the session-lifetime token itself, so None here is correct.
            await LaunchAsync(CancellationToken.None).ContinueOnAnyContext();
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    /// <summary>
    /// Atomically reserves the Disconnected→Launching transition so a launch and an auto-attach (raised
    /// on the detector's background thread) can't both enter a session. Returns false if a session is
    /// already starting or live; the caller should bail.
    /// </summary>
    private bool TryBeginConnecting()
    {
        lock (_sync)
        {
            if (State != ConnectionState.Disconnected)
                return false;

            State = ConnectionState.Launching;
        }

        StateChanged?.Invoke(ConnectionState.Launching);
        UpdateBusy();
        return true;
    }

    private void SetState(ConnectionState state)
    {
        lock (_sync)
        {
            if (State == state)
                return;

            State = state;
        }

        StateChanged?.Invoke(state);
        UpdateBusy();
    }

    // The builder runs the compile off-session; mirror its phase into the session's compiling/busy signals so
    // existing subscribers (the watcher's "Compiling…" phase, the loading UI) see one source of truth.
    private void OnBuildingChanged(bool building)
    {
        CompilingChanged?.Invoke(building);
        UpdateBusy();
    }

    private void UpdateBusy()
    {
        var isBusy = State == ConnectionState.Launching || _builder.Building;
        if (IsBusy == isBusy)
            return;

        IsBusy = isBusy;
        BusyChanged?.Invoke(isBusy);
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
