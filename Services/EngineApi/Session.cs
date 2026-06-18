using Toybox.Studio.Utils;
using System.Net;
using System.Net.Sockets;
using Toybox.Studio.Models;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Project;

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

    // The engine is built in-tree with the project, so this also selects the engine binary: a Debug
    // Studio drives a Debug engine; a Release Studio drives a Release engine.
    private const string BuildConfiguration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private readonly EditorSettings _settings;
    private readonly Locator _locator;
    private readonly ProjectManager _projects;
    private readonly CMakeCompiler _compiler;
    private readonly Logger _log;
    private readonly EngineRpc _engine;
    private readonly object _sync = new();
    private int _isCompiling;

    private Engine? _process;
    private CancellationTokenSource? _pingLoopCts;
    private bool _isStopping;
    private int _connectionLossHandled;
    private DateTime _lastLaunchTimeUtc = DateTime.MinValue;

    public Session(
        EditorSettings settings,
        Locator locator,
        ProjectManager projects,
        CMakeCompiler compiler,
        Logger log,
        EngineRpc engine)
    {
        _settings = settings;
        _locator = locator;
        _projects = projects;
        _compiler = compiler;
        _log = log;
        _engine = engine;
        // The engine is a stable singleton, so wire its streams once: log lines flow into the unified log,
        // and a dropped connection funnels into the same loss handler as a process exit.
        _engine.LogReceived += entry => _log.IngestEngine(entry.Level, entry.Message);
        _engine.Disconnected += OnEngineDisconnected;
        // The engine now runs continuously in editor mode, one per open project: switching projects
        // relaunches it so its world matches.
        _projects.ProjectChanged += OnProjectChanged;
    }

    public event Action<ConnectionState>? StateChanged;

    public event Action<TimeSpan>? PingMeasured;

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
                _log.Error("The project build did not produce a Launcher executable.");
                await TearDownAsync(killProcess: false).ContinueOnAnyContext();
                return;
            }

            var port = GetFreeLoopbackPort();
            _log.Info($"Launching project '{project.Name}' on RPC port {port}...");

            _process = Engine.Start(
                launcherPath,
                project.ModuleName,
                project.AppSettingsPath,
                _settings.Engine.HideEngineWindow,
                port);
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
            _log.Error("Open a project to compile.");
            return false;
        }

        var engineSource = _locator.EngineSourcePath;
        if (engineSource is null)
        {
            _log.Error("Engine source has not been located; use Locate Engine first.");
            return false;
        }

        if (Interlocked.Exchange(ref _isCompiling, 1) == 1)
        {
            _log.Warning("A compile is already running.");
            return false;
        }

        UpdateBusy();
        CompilingChanged?.Invoke(true);
        try
        {
            _log.Info($"Compiling project '{project.Name}'...");
            var build = _settings.Build;
            var compiler = CMakeCompiler.ParseCompiler(build.Compiler);

            // Switching the compiler changes the CMake generator, which can't be done in place, so an
            // existing tree configured for a different toolchain is cleared and reconfigured.
            if (CMakeCompiler.IsConfigured(project.BuildDirectory)
                && !CMakeCompiler.MatchesCompiler(project.BuildDirectory, compiler))
            {
                _log.Info(
                    $"Compiler changed to '{build.Compiler}'; reconfiguring from clean "
                        + "(this rebuilds everything)...");
                if (!TryDeleteDirectory(project.BuildDirectory, out var deleteError))
                {
                    _log.Error($"Could not clear the build folder to switch compilers: {deleteError}");
                    return false;
                }
            }

            // Reuse an already-configured tree's own preset; otherwise pick one for the chosen compiler
            // and configure from scratch. Either way the build preset follows the configure preset.
            var configurePreset = CMakeCompiler.IsConfigured(project.BuildDirectory)
                ? CMakeCompiler.ConfiguredPresetOf(project.BuildDirectory)
                : null;

            if (configurePreset is null)
            {
                configurePreset = await _compiler.ResolveConfigurePresetAsync(compiler, ct)
                    .ContinueOnAnyContext();
                _log.Info("Configuring the CMake build (the first time can take a while)...");
                var defines = new Dictionary<string, string>
                {
                    ["TBX_ENGINE_DIR"] = engineSource.Replace('\\', '/'),
                };
                if (!await _compiler.ConfigureAsync(
                        project.RootDirectory,
                        configurePreset,
                        defines,
                        ct).ContinueOnAnyContext())
                {
                    _log.Error("CMake configure failed.");
                    return false;
                }
            }

            var buildPreset = CMakeCompiler.BuildPreset(configurePreset, configuration);
            if (!await _compiler.BuildAsync(
                    project.RootDirectory, project.BuildDirectory, buildPreset,
                    build.Parallel, build.Verbose, ct)
                    .ContinueOnAnyContext())
            {
                _log.Error("Project build failed.");
                return false;
            }

            _log.Info($"Project '{project.Name}' compiled.");
            return true;
        }
        finally
        {
            Interlocked.Exchange(ref _isCompiling, 0);
            CompilingChanged?.Invoke(false);
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
            _log.Error("Open a project to ship.");
            return;
        }

        if (_locator.EngineSourcePath is null)
        {
            _log.Error("Engine source has not been located; use Locate Engine first.");
            return;
        }

        if (!await CompileProjectAsync(configuration, ct).ContinueOnAnyContext())
            return;

        try
        {
            var launcherPath = FindProjectLauncher(project.BuildDirectory, configuration);
            if (launcherPath is null)
            {
                _log.Error($"The {configuration} build did not produce a Launcher executable.");
                return;
            }

            Directory.CreateDirectory(outputDirectory);

            // Copy the engine + launcher output the project runs against.
            var configurationBin = Path.GetDirectoryName(launcherPath)!;
            _log.Info($"Copying {configuration} build to '{outputDirectory}'...");
            CopyDirectory(configurationBin, outputDirectory);

            // Bring the project's assets and settings along so the standalone build can run.
            if (Directory.Exists(project.AssetsDirectory))
                CopyDirectory(project.AssetsDirectory, Path.Combine(outputDirectory, "Assets"));

            var settingsDestination =
                Path.Combine(outputDirectory, Path.GetFileName(project.AppSettingsPath));
            if (File.Exists(project.AppSettingsPath))
                File.Copy(project.AppSettingsPath, settingsDestination, overwrite: true);

            var exportedLauncher = Path.Combine(outputDirectory, Path.GetFileName(launcherPath));
            _log.Info($"Launching standalone {configuration} build from '{exportedLauncher}'...");
            Engine.StartStandalone(exportedLauncher, project.ModuleName, settingsDestination);
            _log.Info($"Standalone {configuration} build launched.");
        }
        catch (Exception exception)
        {
            _log.Error($"Ship ({configuration}) failed: {exception.Message}");
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
        if (!TryBeginConnecting())
            return;

        Kind = SessionKind.Attached;
        _lastLaunchTimeUtc = DateTime.UtcNow;
        Interlocked.Exchange(ref _connectionLossHandled, 0);
        try
        {
            _log.Info($"Attaching to running engine on :{port}...");
            var connect = await _engine.ConnectAsync(port, AttachConnectTimeout, CancellationToken.None)
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
        await StopAsync().ContinueOnAnyContext();
    }

    private void CompleteConnection(Hello hello)
    {
        ConnectedAppName = hello.App;
        SetState(ConnectionState.Connected);
        // From now on, studio log lines also flow into the engine's unified log, and the engine
        // console's colors track the editor theme.
        _log.SetEngineForwarder(_engine.WriteLogAsync);
        _log.SetLogColorSink(_engine.SetLogColorsAsync);
        _log.Info(
            $"Connected to {hello.Engine} (app '{hello.App}', protocol v{hello.ProtocolVersion}).");

        _pingLoopCts = new CancellationTokenSource();
        RunPingLoopAsync(_pingLoopCts.Token).FireAndForget();
    }

    private async Task RunPingLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PingInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ContinueOnAnyContext())
            {
                var ping = await _engine.PingAsync(ct).ContinueOnAnyContext();
                if (ping.Success)
                    PingMeasured?.Invoke(ping.Value);
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
        await TearDownAsync(killProcess: wasOwned).ContinueOnAnyContext();

        var shouldRestart = wasOwned
            && _settings.Engine.RestartOnCrash
            && DateTime.UtcNow - _lastLaunchTimeUtc > MinimumUptimeForRestart;
        if (shouldRestart)
        {
            _log.Warning("Engine connection lost; restarting the engine...");
            await LaunchAsync(CancellationToken.None).ContinueOnAnyContext();
        }
        else if (!wasOwned)
        {
            _log.Info("Connection to the attached engine was lost.");
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
        Engine? process;
        CancellationTokenSource? pingLoopCts;
        lock (_sync)
        {
            if (State == ConnectionState.Disconnected && _process is null && !_engine.IsConnected)
                return;

            process = _process;
            pingLoopCts = _pingLoopCts;
            _process = null;
            _pingLoopCts = null;
        }

        // We own this disconnect, so flag the loss as handled before tearing the connection down: the
        // engine's Disconnected event (fired by Disconnect below) must not be mistaken for a crash.
        Interlocked.Exchange(ref _connectionLossHandled, 1);
        pingLoopCts?.Cancel();
        pingLoopCts?.Dispose();
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

        RestartForProjectAsync().FireAndForget();
    }

    private async Task RestartForProjectAsync()
    {
        await StopAsync().ContinueOnAnyContext();
        await LaunchAsync(CancellationToken.None).ContinueOnAnyContext();
    }

    /// <summary>
    /// Stops and relaunches the current project's engine, recompiling as part of launch (a compiler change
    /// clean-reconfigures inside <see cref="CompileProjectAsync(CancellationToken)"/>). Used when an editor
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

    private void UpdateBusy()
    {
        var isBusy = State == ConnectionState.Launching || _isCompiling == 1;
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
