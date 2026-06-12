using System.Net;
using System.Net.Sockets;

namespace Toybox.Studio.Services;

/// <summary>
/// Whether the current session owns the engine process or attached to an existing one.
/// </summary>
public enum EngineSessionKind
{
    None,
    Owned,
    Attached,
}

/// <summary>
/// Coordinates one engine session — either launching and owning an engine process, or attaching
/// to an already-running instance without taking it over. Owns the RPC client, keeps a ping loop
/// alive, and tears everything down when either side goes away.
/// </summary>
public sealed class EngineSession : IAsyncDisposable
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinimumUptimeForRestart = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AttachConnectTimeout = TimeSpan.FromSeconds(5);

    // The engine is built in-tree with the project, so this also selects the engine binary: a Debug
    // Studio drives a Debug engine; a Release Studio drives a Release engine.
    private const string BuildConfiguration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private readonly Settings _settings;
    private readonly EngineLocator _locator;
    private readonly ProjectManager _projects;
    private readonly CMakeCompiler _compiler;
    private readonly Logger _log;
    private readonly object _sync = new();
    private int _isCompiling;

    private EngineProcess? _process;
    private EngineRpcClient? _client;
    private CancellationTokenSource? _pingLoopCts;
    private bool _isStopping;
    private int _connectionLossHandled;
    private DateTime _lastLaunchTimeUtc = DateTime.MinValue;

    public EngineSession(
        Settings settings,
        EngineLocator locator,
        ProjectManager projects,
        CMakeCompiler compiler,
        Logger log)
    {
        _settings = settings;
        _locator = locator;
        _projects = projects;
        _compiler = compiler;
        _log = log;
    }

    public event Action<EngineConnectionState>? StateChanged;

    public event Action<TimeSpan>? PingMeasured;

    /// <summary>
    /// Raised when launch/compile work starts or finishes (drives loading UI).
    /// </summary>
    public event Action<bool>? BusyChanged;

    public event Action<bool>? PausedChanged;

    public EngineConnectionState State { get; private set; } = EngineConnectionState.Disconnected;

    public EngineSessionKind Kind { get; private set; } = EngineSessionKind.None;

    public bool IsBusy { get; private set; }

    public bool IsPaused { get; private set; }

    public string? ConnectedAppName { get; private set; }

    /// <summary>
    /// The live RPC client, or null while disconnected.
    /// </summary>
    public EngineRpcClient? Client => _client;

    /// <summary>
    /// Compiles, launches, and owns the current project's engine process. Failures are reported
    /// as studio log lines.
    /// </summary>
    public async Task LaunchAsync(CancellationToken ct)
    {
        if (State != EngineConnectionState.Disconnected)
            return;

        var project = _projects.CurrentProject;
        if (project is null)
        {
            _log.Log("error", "Open a project to launch.");
            return;
        }

        SetState(EngineConnectionState.Launching);
        Kind = EngineSessionKind.Owned;
        _lastLaunchTimeUtc = DateTime.UtcNow;
        _log.Info($"=== Session: {project.Name} ===");
        Interlocked.Exchange(ref _connectionLossHandled, 0);
        try
        {
            if (!await CompileProjectAsync(ct).ContinueOnAnyContext())
            {
                await TearDownAsync(killProcess: false).ContinueOnAnyContext();
                return;
            }

            var launcherPath = FindProjectLauncher(project.BuildDirectory, BuildConfiguration);
            if (launcherPath is null)
            {
                _log.Log("error", "The project build did not produce a Launcher executable.");
                await TearDownAsync(killProcess: false).ContinueOnAnyContext();
                return;
            }

            var port = GetFreeLoopbackPort();
            _log.Log("info", $"Launching project '{project.Name}' on RPC port {port}...");

            _process = EngineProcess.Start(
                launcherPath,
                project.ModuleName,
                project.AppSettingsPath,
                _settings.Editor.Engine.HideEngineWindow,
                port);
            _process.Exited += OnProcessExited;
            _log.Log("info", $"Engine process started (pid {_process.Id}).");

            var client = CreateClient();
            var hello = await client.ConnectAsync(
                port,
                TimeSpan.FromSeconds(_settings.Editor.Engine.ConnectTimeoutSeconds),
                ct).ContinueOnAnyContext();

            CompleteConnection(hello);
        }
        catch (Exception exception)
        {
            _log.Log("error", $"Launch failed: {exception.Message}");
            await TearDownAsync(killProcess: true).ContinueOnAnyContext();
        }
    }

    /// <summary>
    /// Configures (once) and builds the current project via CMake in the studio's build configuration
    /// (Debug or Release, matching how the editor itself was built).
    /// </summary>
    public Task<bool> CompileProjectAsync(CancellationToken ct) =>
        CompileProjectAsync(BuildConfiguration, ct);

    /// <summary>
    /// Configures (once) and builds the current project via CMake in the given configuration.
    /// </summary>
    public async Task<bool> CompileProjectAsync(string configuration, CancellationToken ct)
    {
        var project = _projects.CurrentProject;
        if (project is null)
        {
            _log.Log("error", "Open a project to compile.");
            return false;
        }

        var engineSource = _locator.EngineSourcePath;
        if (engineSource is null)
        {
            _log.Log("error", "Engine source has not been located; use Locate Engine first.");
            return false;
        }

        if (Interlocked.Exchange(ref _isCompiling, 1) == 1)
        {
            _log.Log("warning", "A compile is already running.");
            return false;
        }

        UpdateBusy();
        try
        {
            _log.Log("info", $"Compiling project '{project.Name}'...");
            var build = _settings.Editor.Build;
            var compiler = CMakeCompiler.ParseCompiler(build.Compiler);

            // Switching the compiler changes the CMake generator, which can't be done in place, so an
            // existing tree configured for a different toolchain is cleared and reconfigured.
            if (CMakeCompiler.IsConfigured(project.BuildDirectory)
                && !CMakeCompiler.MatchesCompiler(project.BuildDirectory, compiler))
            {
                _log.Log(
                    "info",
                    $"Compiler changed to '{build.Compiler}'; reconfiguring from clean "
                        + "(this rebuilds everything)...");
                if (!TryDeleteDirectory(project.BuildDirectory, out var deleteError))
                {
                    _log.Log("error", $"Could not clear the build folder to switch compilers: {deleteError}");
                    return false;
                }
            }

            if (!CMakeCompiler.IsConfigured(project.BuildDirectory))
            {
                _log.Log("info", "Configuring the CMake build (the first time can take a while)...");
                var defines = new Dictionary<string, string>
                {
                    ["TBX_ENGINE_DIR"] = engineSource.Replace('\\', '/'),
                };
                if (!await _compiler.ConfigureAsync(
                        project.RootDirectory,
                        project.BuildDirectory,
                        defines,
                        compiler,
                        _log.Log,
                        ct).ContinueOnAnyContext())
                {
                    _log.Log("error", "CMake configure failed.");
                    return false;
                }
            }

            if (!await _compiler.BuildAsync(
                    project.BuildDirectory, configuration, build.Parallel, build.Verbose, _log.Log, ct)
                    .ContinueOnAnyContext())
            {
                _log.Log("error", "Project build failed.");
                return false;
            }

            _log.Log("info", $"Project '{project.Name}' compiled.");
            return true;
        }
        finally
        {
            Interlocked.Exchange(ref _isCompiling, 0);
            UpdateBusy();
        }
    }

    private static string? FindProjectLauncher(string buildDirectory, string configuration)
    {
        var binDirectory = Path.Combine(buildDirectory, "bin");
        if (!Directory.Exists(binDirectory))
            return null;

        // Prefer the launcher for the configuration we just built; fall back to the newest anywhere.
        var preferred = Path.Combine(binDirectory, configuration, "Launcher.exe");
        if (File.Exists(preferred))
            return preferred;

        return Directory.EnumerateFiles(binDirectory, "Launcher.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>
    /// Ships the current project in the given configuration (Debug or Release): builds it, copies the
    /// engine + launcher (and the project's assets and settings) into <paramref name="outputDirectory"/>,
    /// then launches it standalone so the user can test the shipped build independently of the editor.
    /// Failures are reported as studio log lines.
    /// </summary>
    public async Task ShipAsync(string configuration, string outputDirectory, CancellationToken ct)
    {
        var project = _projects.CurrentProject;
        if (project is null)
        {
            _log.Log("error", "Open a project to ship.");
            return;
        }

        if (_locator.EngineSourcePath is null)
        {
            _log.Log("error", "Engine source has not been located; use Locate Engine first.");
            return;
        }

        if (!await CompileProjectAsync(configuration, ct).ContinueOnAnyContext())
            return;

        try
        {
            var launcherPath = FindProjectLauncher(project.BuildDirectory, configuration);
            if (launcherPath is null)
            {
                _log.Log("error", $"The {configuration} build did not produce a Launcher executable.");
                return;
            }

            Directory.CreateDirectory(outputDirectory);

            // Copy the engine + launcher output the project runs against.
            var configurationBin = Path.GetDirectoryName(launcherPath)!;
            _log.Log("info", $"Copying {configuration} build to '{outputDirectory}'...");
            CopyDirectory(configurationBin, outputDirectory);

            // Bring the project's assets and settings along so the standalone build can run.
            if (Directory.Exists(project.AssetsDirectory))
                CopyDirectory(project.AssetsDirectory, Path.Combine(outputDirectory, "Assets"));

            var settingsDestination =
                Path.Combine(outputDirectory, Path.GetFileName(project.AppSettingsPath));
            if (File.Exists(project.AppSettingsPath))
                File.Copy(project.AppSettingsPath, settingsDestination, overwrite: true);

            var exportedLauncher = Path.Combine(outputDirectory, Path.GetFileName(launcherPath));
            _log.Log("info", $"Launching standalone {configuration} build from '{exportedLauncher}'...");
            EngineProcess.StartStandalone(exportedLauncher, project.ModuleName, settingsDestination);
            _log.Log("info", $"Standalone {configuration} build launched.");
        }
        catch (Exception exception)
        {
            _log.Log("error", $"Ship ({configuration}) failed: {exception.Message}");
        }
    }

    private static bool TryDeleteDirectory(string path, out string? error)
    {
        error = null;
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
    }

    /// <summary>
    /// Attaches to an engine that is already running. The instance is never taken over: the
    /// session only reads data and injects the editor view, and stopping merely detaches.
    /// </summary>
    public async Task AttachAsync(int port)
    {
        if (State != EngineConnectionState.Disconnected)
            return;

        SetState(EngineConnectionState.Launching);
        Kind = EngineSessionKind.Attached;
        _lastLaunchTimeUtc = DateTime.UtcNow;
        Interlocked.Exchange(ref _connectionLossHandled, 0);
        try
        {
            _log.Log("info", $"Attaching to running engine on :{port}...");
            var client = CreateClient();
            var hello = await client.ConnectAsync(port, AttachConnectTimeout, CancellationToken.None)
                .ContinueOnAnyContext();

            CompleteConnection(hello);
            _log.Log("info", $"Attached to running engine on :{port}.");
        }
        catch (Exception exception)
        {
            _log.Log("error", $"Attach failed: {exception.Message}");
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
            if (State == EngineConnectionState.Disconnected || _isStopping)
                return;

            _isStopping = true;
        }

        try
        {
            if (Kind == EngineSessionKind.Attached)
            {
                _log.Log("info", "Detaching from engine; it keeps running.");
                if (IsPaused)
                    await SetPausedAsync(false).ContinueOnAnyContext();

                await TearDownAsync(killProcess: false).ContinueOnAnyContext();
                return;
            }

            var client = _client;
            var process = _process;
            if (client is { IsConnected: true } && process is not null)
            {
                _log.Log("info", "Requesting engine shutdown...");
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await client.RequestShutdownAsync(cts.Token).ContinueOnAnyContext();
                }
                catch (Exception)
                {
                    // The engine may close the connection before replying; the process wait below
                    // decides whether a kill is still needed.
                }

                if (!await process.WaitForExitAsync(GracefulExitTimeout).ContinueOnAnyContext())
                    _log.Log("warning", "Engine did not exit in time; killing the process.");
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
        await StopAsync().ContinueOnAnyContext();
    }

    private EngineRpcClient CreateClient()
    {
        var client = new EngineRpcClient();
        client.LogReceived += entry => _log.IngestEngineLog(entry.Level, entry.Message);
        client.Disconnected += OnClientDisconnected;
        _client = client;
        return client;
    }

    private void CompleteConnection(EngineHello hello)
    {
        ConnectedAppName = hello.App;
        SetState(EngineConnectionState.Connected);
        // From now on, studio log lines also flow into the engine's unified log, and the engine
        // console's colors track the editor theme.
        _log.SetEngineForwarder(_client!.WriteEditorLogAsync);
        _log.SetLogColorSink(_client!.SetLogColorsAsync);
        _log.Log(
            "info",
            $"Connected to {hello.Engine} (app '{hello.App}', protocol v{hello.ProtocolVersion}).");

        _pingLoopCts = new CancellationTokenSource();
        RunPingLoopAsync(_client!, _pingLoopCts.Token).FireAndForget();
    }

    private async Task RunPingLoopAsync(EngineRpcClient client, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PingInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ContinueOnAnyContext())
            {
                var roundTrip = await client.PingAsync(ct).ContinueOnAnyContext();
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
        _log.Log(
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

        HandleConnectionLossAsync().FireAndForget();
    }

    private async Task HandleConnectionLossAsync()
    {
        var wasOwned = Kind == EngineSessionKind.Owned;
        await TearDownAsync(killProcess: wasOwned).ContinueOnAnyContext();

        var shouldRestart = wasOwned
            && _settings.Editor.Engine.RestartOnCrash
            && DateTime.UtcNow - _lastLaunchTimeUtc > MinimumUptimeForRestart;
        if (shouldRestart)
        {
            _log.Log("warning", "Engine connection lost; restarting the engine...");
            await LaunchAsync(CancellationToken.None).ContinueOnAnyContext();
        }
        else if (!wasOwned)
        {
            _log.Log("info", "Connection to the attached engine was lost.");
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
            await client.DisposeAsync().ContinueOnAnyContext();
        }

        if (process is not null)
        {
            process.Exited -= OnProcessExited;
            if (killProcess && process.IsRunning)
            {
                _log.Log("warning", "Killing engine process.");
                // Wait for it to actually exit: the engine binaries are built in-tree, so a lingering
                // handle would make the next compile's relink fail with a permission-denied error.
                await process.KillAndWaitAsync(GracefulExitTimeout).ContinueOnAnyContext();
            }

            process.Dispose();
        }

        _log.SetEngineForwarder(null);
        _log.SetLogColorSink(null);
        ConnectedAppName = null;
        Kind = EngineSessionKind.None;
        if (IsPaused)
        {
            IsPaused = false;
            PausedChanged?.Invoke(false);
        }

        SetState(EngineConnectionState.Disconnected);
    }

    /// <summary>
    /// Pauses or resumes the connected engine's simulation.
    /// </summary>
    public async Task SetPausedAsync(bool isPaused)
    {
        var client = _client;
        if (client is not { IsConnected: true })
            return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.SetPausedAsync(isPaused, cts.Token).ContinueOnAnyContext();
            IsPaused = isPaused;
            PausedChanged?.Invoke(isPaused);
            _log.Log("info", isPaused ? "Engine paused." : "Engine resumed.");
        }
        catch (Exception exception)
        {
            _log.Log("error", $"Pause request failed: {exception.Message}");
        }
    }

    private void SetState(EngineConnectionState state)
    {
        if (State == state)
            return;

        State = state;
        StateChanged?.Invoke(state);
        UpdateBusy();
    }

    private void UpdateBusy()
    {
        var isBusy = State == EngineConnectionState.Launching || _isCompiling == 1;
        if (IsBusy == isBusy)
            return;

        IsBusy = isBusy;
        BusyChanged?.Invoke(isBusy);
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
