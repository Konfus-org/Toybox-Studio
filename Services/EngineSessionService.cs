using System.Net;
using System.Net.Sockets;

namespace Toybox.Studio.Services;

/// <summary>
/// Coordinates one engine session: launches the process, connects the RPC client, keeps a ping
/// loop alive, and tears everything down when either side goes away.
/// </summary>
public sealed class EngineSessionService : IAsyncDisposable
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinimumUptimeForRestart = TimeSpan.FromSeconds(10);

    private readonly EngineLaunchOptions _options;
    private readonly object _sync = new();

    private EngineProcess? _process;
    private EngineRpcClient? _client;
    private CancellationTokenSource? _pingLoopCts;
    private bool _isStopping;
    private int _connectionLossHandled;
    private DateTime _lastLaunchTimeUtc = DateTime.MinValue;

    public EngineSessionService(EngineLaunchOptions options)
    {
        _options = options;
    }

    public event Action<EngineConnectionState>? StateChanged;

    public event Action<EngineLogEntry>? LogReceived;

    public event Action<TimeSpan>? PingMeasured;

    public EngineConnectionState State { get; private set; } = EngineConnectionState.Disconnected;

    public string? ConnectedAppName { get; private set; }

    /// <summary>The live RPC client, or null while disconnected.</summary>
    public EngineRpcClient? Client => _client;

    /// <summary>Launches the engine and connects. Failures are reported as studio log lines.</summary>
    public async Task LaunchAsync(CancellationToken ct)
    {
        if (State != EngineConnectionState.Disconnected)
            return;

        SetState(EngineConnectionState.Launching);
        _lastLaunchTimeUtc = DateTime.UtcNow;
        Interlocked.Exchange(ref _connectionLossHandled, 0);
        try
        {
            var port = GetFreeLoopbackPort();
            EmitStudioLog("info", $"Launching engine on RPC port {port}...");

            _process = EngineProcess.Start(_options, port);
            _process.Exited += OnProcessExited;
            EmitStudioLog("info", $"Engine process started (pid {_process.Id}).");

            var client = new EngineRpcClient();
            client.LogReceived += entry => LogReceived?.Invoke(entry);
            client.Disconnected += OnClientDisconnected;
            _client = client;

            var hello = await client.ConnectAsync(
                port,
                TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds),
                ct).ConfigureAwait(false);

            ConnectedAppName = hello.App;
            SetState(EngineConnectionState.Connected);
            EmitStudioLog("info", $"Connected to {hello.Engine} (app '{hello.App}', protocol v{hello.ProtocolVersion}).");

            _pingLoopCts = new CancellationTokenSource();
            _ = RunPingLoopAsync(client, _pingLoopCts.Token);
        }
        catch (Exception exception)
        {
            EmitStudioLog("error", $"Launch failed: {exception.Message}");
            await TearDownAsync(killProcess: true).ConfigureAwait(false);
        }
    }

    /// <summary>Stops the session, asking the engine to exit gracefully before killing it.</summary>
    public async Task StopAsync()
    {
        lock (_sync)
        {
            if (State == EngineConnectionState.Disconnected || _isStopping)
                return;

            _isStopping = true;
        }

        try
        {
            var client = _client;
            var process = _process;
            if (client is { IsConnected: true } && process is not null)
            {
                EmitStudioLog("info", "Requesting engine shutdown...");
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await client.RequestShutdownAsync(cts.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The engine may close the connection before replying; the process wait below
                    // decides whether a kill is still needed.
                }

                if (!await process.WaitForExitAsync(GracefulExitTimeout).ConfigureAwait(false))
                    EmitStudioLog("warning", "Engine did not exit in time; killing the process.");
            }

            await TearDownAsync(killProcess: true).ConfigureAwait(false);
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
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunPingLoopAsync(EngineRpcClient client, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PingInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                var roundTrip = await client.PingAsync(ct).ConfigureAwait(false);
                PingMeasured?.Invoke(roundTrip);
            }
        }
        catch (Exception)
        {
            // Cancelled on teardown, or the connection died; the Disconnected handler owns cleanup.
        }
    }

    private void OnProcessExited(int exitCode)
    {
        EmitStudioLog(
            exitCode == 0 ? "info" : "warning",
            $"Engine process exited (code {exitCode}).");
        TryHandleConnectionLoss();
    }

    private void OnClientDisconnected()
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

        _ = HandleConnectionLossAsync();
    }

    private async Task HandleConnectionLossAsync()
    {
        await TearDownAsync(killProcess: true).ConfigureAwait(false);

        var shouldRestart = _options.RestartOnCrash
            && DateTime.UtcNow - _lastLaunchTimeUtc > MinimumUptimeForRestart;
        if (shouldRestart)
        {
            EmitStudioLog("warning", "Engine connection lost; restarting the engine...");
            await LaunchAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private bool IsStopInProgress()
    {
        lock (_sync)
        {
            return _isStopping;
        }
    }

    private async Task TearDownAsync(bool killProcess)
    {
        EngineProcess? process;
        EngineRpcClient? client;
        CancellationTokenSource? pingLoopCts;
        lock (_sync)
        {
            if (State == EngineConnectionState.Disconnected && _process is null && _client is null)
                return;

            process = _process;
            client = _client;
            pingLoopCts = _pingLoopCts;
            _process = null;
            _client = null;
            _pingLoopCts = null;
        }

        pingLoopCts?.Cancel();
        pingLoopCts?.Dispose();

        if (client is not null)
        {
            client.Disconnected -= OnClientDisconnected;
            await client.DisposeAsync().ConfigureAwait(false);
        }

        if (process is not null)
        {
            process.Exited -= OnProcessExited;
            if (killProcess && process.IsRunning)
            {
                EmitStudioLog("warning", "Killing engine process.");
                process.Kill();
            }

            process.Dispose();
        }

        ConnectedAppName = null;
        SetState(EngineConnectionState.Disconnected);
    }

    private void SetState(EngineConnectionState state)
    {
        if (State == state)
            return;

        State = state;
        StateChanged?.Invoke(state);
    }

    private void EmitStudioLog(string level, string message)
    {
        LogReceived?.Invoke(new EngineLogEntry(level, message, Source: "studio"));
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
