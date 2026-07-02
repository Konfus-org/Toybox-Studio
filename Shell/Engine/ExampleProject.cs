using Toybox.Studio.CMake;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Shell;

/// <summary>A project's launch identity: its name, the app module it hosts, and its settings file.</summary>
public sealed record ProjectInfo(string Name, string ModuleName, string AppSettingsPath);

/// <summary>
/// The engine's bundled example project, found beside the located engine source tree
/// (&lt;Toybox&gt;/Engine → &lt;Toybox&gt;/ExampleProject). This is the only project the editor opens for
/// now; a real project manager can replace this later without touching the engine session, which never
/// sees projects at all. The project owns its whole build: it compiles itself through the generic CMake
/// driver (configuring the tree against the located engine first if needed) and locates the launcher the
/// build produced. By convention the CMake target (and so the app module) is named after the project's
/// root folder, and the build tree lives in &lt;root&gt;/build. A re-located engine means a different
/// project, so <see cref="EngineLocated"/> re-dispatches as <see cref="ProjectChanged"/>.
/// </summary>
public sealed class ExampleProject : EventSubscriber, IEventHandler<EngineLocated>
{
    // The engine is built in-tree with the project, so this also selects the engine binary: a Debug Studio
    // drives a Debug engine; a Release Studio a Release engine.
    private const string Configuration =
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private readonly EngineLocator _locator;
    private readonly CMakeCompiler _compiler;
    private readonly Logger _log;

    // At most one build runs at a time; a second caller is turned away rather than running a concurrent
    // CMake invocation over the same tree. The gate also flips the building state on enter/exit.
    private readonly ReentrancyGuard _buildGate = new();
    private bool _isBuilding;

    public ExampleProject(EngineLocator locator, CMakeCompiler compiler, Logger log, EventDispatcher events)
        : base(events)
    {
        _locator = locator;
        _compiler = compiler;
        _log = log;
    }

    public ProjectInfo? Current =>
        Root is { } root
            ? new ProjectInfo(
                Path.GetFileName(root), Path.GetFileName(root), Path.Combine(root, "AppSettings.json"))
            : null;

    // Announces the compile phase as BuildStateChanged, which Engine.State folds into the Compiling phase.
    private bool Building
    {
        set
        {
            if (_isBuilding == value)
                return;

            _isBuilding = value;
            Events.Dispatch(new BuildStateChanged(value));
        }
    }

    // The project's root folder, or null while the engine (and so the example project beside it) isn't located.
    private string? Root
    {
        get
        {
            if (Path.GetDirectoryName(_locator.EngineSourcePath) is not { } toyboxRoot)
                return null;

            var root = Path.Combine(toyboxRoot, "ExampleProject");
            return File.Exists(Path.Combine(root, "AppSettings.json")) ? root : null;
        }
    }

    public void Handle(in EngineLocated evt) => Events.Dispatch(new ProjectChanged());

    /// <summary>
    /// Configures (once) and builds the project via CMake in the studio's build configuration, then
    /// returns the launcher the build produced; null on failure, reported as studio log lines.
    /// </summary>
    public async Task<string?> PrepareLauncherAsync(CancellationToken ct)
    {
        if (Root is not { } root)
        {
            _log.Error("No project is available to compile.");
            return null;
        }

        var engineSource = _locator.EngineSourcePath;
        if (engineSource is null)
        {
            _log.Error("The engine source has not been located; the project cannot be compiled.");
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

        var buildDirectory = Path.Combine(root, "build");
        _log.Info($"Compiling project '{Path.GetFileName(root)}'...");

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
            if (!await _compiler.ConfigureAsync(root, configurePreset, defines, ct).ContinueOnAnyContext())
            {
                _log.Error("CMake configure failed.");
                return null;
            }
        }

        var buildPreset = CMakeCompiler.BuildPreset(configurePreset, Configuration);
        if (!await _compiler
                .BuildAsync(root, buildDirectory, buildPreset, parallel: true, verbose: false, ct)
                .ContinueOnAnyContext())
        {
            _log.Error("Project build failed.");
            return null;
        }

        _log.Info($"Project '{Path.GetFileName(root)}' compiled.");
        return FindLauncher(buildDirectory);
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
}
