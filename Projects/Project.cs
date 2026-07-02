using Toybox.Studio.CMake;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Projects;

/// <summary>
/// A Toybox project the studio has open — a folder carrying the project's settings file (startup
/// prompts the user for one; there is no project manager or persisted settings yet). The project owns
/// its whole build: it compiles itself through the generic CMake driver (configuring the tree against
/// the Toybox engine checkout found beside it first if needed) and locates the launcher the build
/// produced. By convention the CMake target (and so the app module) is named after the project's root
/// folder, and the build tree lives in &lt;root&gt;/build.
/// </summary>
public sealed class Project
{
    /// <summary>The settings file every project carries; its presence is what makes a folder a project.</summary>
    public const string SettingsFileName = "AppSettings.json";

    // The engine is built in-tree with the project, so this also selects the engine binary: a Debug Studio
    // drives a Debug engine; a Release Studio a Release engine.
    private const string Configuration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private readonly string _root;
    private readonly string _configuredEngineSource;
    private readonly CMakeCompiler _compiler;
    private readonly Logger _log;
    private readonly EventDispatcher _events;

    // At most one build runs at a time; a second caller is turned away rather than running a concurrent
    // CMake invocation over the same tree. The gate also flips the building state on enter/exit.
    private readonly ReentrancyGuard _buildGate = new();
    private bool _isBuilding;

    /// <param name="configuredEngineSource">The engine source path from the editor settings; empty means
    /// unconfigured, in which case the engine is found by climbing the project's ancestor folders.</param>
    public Project(string root, string configuredEngineSource, Logger log, EventDispatcher events)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _configuredEngineSource = configuredEngineSource;
        _log = log;
        _events = events;
        _compiler = new CMakeCompiler(new CommandRunner(log), log);
    }

    public string Name => Path.GetFileName(_root);

    /// <summary>The app module the engine hosts, named after the project's root folder by convention.</summary>
    public string ModuleName => Name;

    public string AppSettingsPath => Path.Combine(_root, SettingsFileName);

    // Announces the compile phase as BuildStateChanged, which Engine.State folds into the Compiling phase.
    private bool Building
    {
        set
        {
            if (_isBuilding == value)
                return;

            _isBuilding = value;
            _events.Dispatch(new BuildStateChanged(value));
        }
    }

    /// <summary>Whether the folder is a Toybox project (it carries the project settings file).</summary>
    public static bool IsProjectDirectory(string path) => File.Exists(Path.Combine(path, SettingsFileName));

    /// <summary>
    /// Configures (once) and builds the project via CMake in the studio's build configuration, then
    /// returns the launcher the build produced; null on failure, reported as studio log lines.
    /// </summary>
    public async Task<string?> PrepareLauncherAsync(CancellationToken ct)
    {
        if (FindEngineSource() is not { } engineSource)
        {
            _log.Error(
                "No Toybox engine found: set the engine source path in the editor settings, or place "
                    + $"the project beside an Engine checkout (in one of '{_root}'s ancestor folders).");
            return null;
        }

        // The gate turns the building state on now and off when this scope disposes; a concurrent caller
        // gets a null scope and bails.
        using var buildScope = _buildGate.TryEnter(building => Building = building);
        if (buildScope is null)
        {
            _log.Warning("A compile is already running.");
            return null;
        }

        var buildDirectory = Path.Combine(_root, "build");
        _log.Info($"Compiling project '{Name}' against the engine at {engineSource}...");

        // Reuse an already-configured tree's own preset; otherwise pick one for the machine's toolchain
        // and configure from scratch (the first configure can take a while).
        var configurePreset = CMakeCompiler.IsConfigured(buildDirectory)
            ? CMakeCompiler.ConfiguredPresetOf(buildDirectory)
            : null;
        if (configurePreset is null)
        {
            configurePreset = await _compiler
                .ResolveConfigurePresetAsync(CompilerPreference.Auto, ct).ContinueOnAnyContext();
            _log.Info("Configuring the CMake build (the first time can take a while)...");
            var defines = new Dictionary<string, string>
            {
                ["TBX_ENGINE_DIR"] = engineSource.Replace('\\', '/'),
            };
            if (!await _compiler.ConfigureAsync(_root, configurePreset, defines, ct).ContinueOnAnyContext())
            {
                _log.Error("CMake configure failed.");
                return null;
            }
        }

        var buildPreset = CMakeCompiler.BuildPreset(configurePreset, Configuration);
        if (!await _compiler
                .BuildAsync(_root, buildDirectory, buildPreset, parallel: true, verbose: false, ct)
                .ContinueOnAnyContext())
        {
            _log.Error("Project build failed.");
            return null;
        }

        _log.Info($"Project '{Name}' compiled.");
        return FindLauncher(buildDirectory);
    }

    /// <summary>
    /// The engine source tree the project compiles against: the configured settings path when it points at
    /// a real engine, otherwise found by climbing the project's ancestors for an Engine checkout (a project
    /// lives beside the engine in a Toybox root, e.g. &lt;Toybox&gt;/ExampleProject beside
    /// &lt;Toybox&gt;/Engine); null when neither yields one.
    /// </summary>
    private string? FindEngineSource()
    {
        if (_configuredEngineSource.Length > 0)
        {
            if (IsEngineSourceDirectory(_configuredEngineSource))
                return _configuredEngineSource;

            _log.Warning(
                $"The configured engine source path '{_configuredEngineSource}' is not an engine "
                    + "checkout; searching beside the project instead.");
        }

        for (var directory = new DirectoryInfo(_root).Parent;
            directory is not null;
            directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Engine");
            if (IsEngineSourceDirectory(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Locates the launcher the build produced, preferring this configuration's output and falling back
    /// to the newest launcher anywhere under the build's bin folder; null (logged) when none exists.
    /// </summary>
    private string? FindLauncher(string buildDirectory)
    {
        var binDirectory = Path.Combine(buildDirectory, "bin");
        var preferred = Path.Combine(binDirectory, Configuration, "Launcher.exe");
        var launcher = File.Exists(preferred)
            ? preferred
            : Directory.Exists(binDirectory)
                ? Directory.EnumerateFiles(binDirectory, "Launcher.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;

        if (launcher is null)
            _log.Error("The project build did not produce a Launcher executable.");
        return launcher;
    }

    private static bool IsEngineSourceDirectory(string path)
    {
        return File.Exists(Path.Combine(path, "CMakeLists.txt"))
            && Directory.Exists(Path.Combine(path, "engine"))
            && Directory.Exists(Path.Combine(path, "tools", "cmake"));
    }
}
