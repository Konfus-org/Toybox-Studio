using Toybox.Studio.Utils;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Settings;

namespace Toybox.Studio.Services.Project;

/// <summary>
/// Owns the project's native build operations, separate from the engine <see cref="Session"/> that runs the
/// resulting process: configuring + building the current project via CMake (<see cref="BuildAsync(CancellationToken)"/>),
/// and shipping a standalone export (<see cref="ShipAsync"/>). A single build runs at a time (the
/// <see cref="Building"/> guard), and <see cref="BuildingChanged"/> lets the session surface "Compiling…" in
/// its busy state. Failures are reported as studio log lines. The session calls <see cref="BuildAsync(CancellationToken)"/>
/// as part of launch; the Build menu and script hot-reload call it directly.
/// </summary>
public sealed class ProjectBuilder
{
    // The engine is built in-tree with the project, so this also selects the engine binary: a Debug Studio
    // drives a Debug engine; a Release Studio a Release engine.
    public const string BuildConfiguration =
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

    // At most one build runs at a time; a second caller (a Build-menu click during a launch compile, say) is
    // turned away rather than running a concurrent CMake invocation over the same tree. The gate also flips
    // Building on enter/exit, so the busy flag tracks the scope without a try/finally.
    private readonly ReentrancyGuard _buildGate = new();
    private bool _isBuilding;

    public ProjectBuilder(
        SettingsManager settings, Locator locator, ProjectManager projects, CMakeCompiler compiler, Logger log)
    {
        _settings = settings.Settings;
        _locator = locator;
        _projects = projects;
        _compiler = compiler;
        _log = log;
    }

    /// <summary>Raised when a build starts (true) or finishes (false); drives the session's "Compiling" phase.</summary>
    public event Action<bool>? BuildingChanged;

    /// <summary>
    /// Whether a project build is currently running. Set only by the build gate (true on enter, false on
    /// scope dispose); the setter raises <see cref="BuildingChanged"/> so subscribers track it.
    /// </summary>
    public bool Building
    {
        get => _isBuilding;
        private set
        {
            if (_isBuilding == value)
                return;

            _isBuilding = value;
            BuildingChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// Locates the launcher the given build configuration produced under <paramref name="buildDirectory"/>,
    /// preferring that configuration's output and falling back to the newest launcher anywhere; null when none
    /// was produced.
    /// </summary>
    public static string? FindProjectLauncher(string buildDirectory, string configuration)
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
    /// Configures (once) and builds the current project via CMake in the studio's build configuration
    /// (Debug or Release, matching how the editor itself was built).
    /// </summary>
    public Task<bool> BuildAsync(CancellationToken ct) => BuildAsync(BuildConfiguration, ct);

    /// <summary>
    /// Configures (once) and builds the current project via CMake in the given configuration.
    /// </summary>
    public async Task<bool> BuildAsync(string configuration, CancellationToken ct)
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

        // The gate turns Building on now and off when this scope disposes (every exit path below), so the
        // busy flag is handled without a try/finally; a concurrent caller gets a null scope and bails.
        using var buildScope = _buildGate.TryEnter(building => Building = building);
        if (buildScope is null)
        {
            _log.Warning("A compile is already running.");
            return false;
        }

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

        if (!await BuildAsync(configuration, ct).ContinueOnAnyContext())
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
}
