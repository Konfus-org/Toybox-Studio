using Toybox.Studio.CMake;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Projects;

/// <summary>
/// Builds a project in a given <see cref="BuildMode"/>: configures (once) and compiles its CMake tree
/// through the generic CMake driver — against the engine checkout from the editor settings, or the one
/// found beside the project — and returns the launcher the build produced. By convention the CMake
/// target (and so the app module) is named after the project's root folder, and the build tree lives
/// in &lt;root&gt;/build. Failures come back as a failed <see cref="Result{T}"/> for the caller to
/// report; progress is narrated as studio log lines.
/// </summary>
public sealed class ProjectBuilder
{
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
    public ProjectBuilder(string configuredEngineSource, Logger log, EventDispatcher events)
    {
        _configuredEngineSource = configuredEngineSource;
        _log = log;
        _events = events;
        _compiler = new CMakeCompiler(new CommandRunner(log), log);
    }

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

    /// <summary>
    /// Configures (once) and builds the project via CMake in the given mode, then returns the launcher
    /// the build produced.
    /// </summary>
    public async Task<Result<string>> BuildAsync(Project project, BuildMode mode, CancellationToken ct)
    {
        if (!ProjectLoader.IsProjectDirectory(project.Path))
        {
            return Result<string>.Fail(
                $"'{project.Path}' is not a Toybox project (it has no {ProjectLoader.SettingsFileName}).");
        }

        if (FindEngineSource(project.Path) is not { } engineSource)
        {
            return Result<string>.Fail(
                "No Toybox engine found: set the engine source path in the editor settings, or place "
                    + $"the project beside an Engine checkout (in one of '{project.Path}'s ancestor folders).");
        }

        // The gate turns the building state on now and off when this scope disposes; a concurrent caller
        // gets a null scope and bails.
        using var buildScope = _buildGate.TryEnter(building => Building = building);
        if (buildScope is null)
            return Result<string>.Fail("A compile is already running.");

        var buildDirectory = Path.Combine(project.Path, "build");
        _log.Info($"Compiling project '{project.Name}' against the engine at {engineSource}...");

        // Reuse an already-configured tree's own preset; otherwise pick one for the machine's toolchain
        // and configure from scratch (the first configure can take a while).
        var configurePreset = CMakeCompiler.ConfiguredPresetOf(buildDirectory);
        if (configurePreset is null)
        {
            configurePreset = await _compiler
                .ResolveConfigurePresetAsync(CompilerPreference.Auto, ct).ContinueOnAnyContext();
            _log.Info("Configuring the CMake build (the first time can take a while)...");
            var defines = new Dictionary<string, string>
            {
                ["TBX_ENGINE_DIR"] = engineSource.Replace('\\', '/'),
            };
            if (!await _compiler
                    .ConfigureAsync(project.Path, configurePreset, defines, ct).ContinueOnAnyContext())
            {
                // A failed configure still writes a cache, which would make this tree read as configured
                // and skip this step on every later build; clear it so the next attempt starts clean.
                CMakeCompiler.Clean(buildDirectory);
                return Result<string>.Fail("CMake configure failed.");
            }
        }

        var buildPreset = CMakeCompiler.BuildPreset(configurePreset, mode.ToString());
        if (!await _compiler
                .BuildAsync(project.Path, buildDirectory, buildPreset, parallel: true, verbose: false, ct)
                .ContinueOnAnyContext())
        {
            return Result<string>.Fail("Project build failed.");
        }

        _log.Info($"Project '{project.Name}' compiled.");
        return FindLauncher(buildDirectory, mode);
    }

    /// <summary>
    /// The engine source tree the project compiles against: the configured settings path when it points at
    /// a real engine, otherwise found by climbing the project's ancestors for an Engine checkout (a project
    /// lives beside the engine in a Toybox root, e.g. &lt;Toybox&gt;/ExampleProject beside
    /// &lt;Toybox&gt;/Engine); null when neither yields one.
    /// </summary>
    private string? FindEngineSource(string root)
    {
        if (_configuredEngineSource.Length > 0)
        {
            if (IsEngineSourceDirectory(_configuredEngineSource))
                return _configuredEngineSource;

            _log.Warning(
                $"The configured engine source path '{_configuredEngineSource}' is not an engine "
                    + "checkout; searching beside the project instead.");
        }

        for (var directory = new DirectoryInfo(root).Parent;
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
    /// Locates the launcher the build produced, preferring the built mode's output and falling back
    /// to the newest launcher anywhere under the build's bin folder.
    /// </summary>
    private static Result<string> FindLauncher(string buildDirectory, BuildMode mode)
    {
        var binDirectory = Path.Combine(buildDirectory, "bin");
        var preferred = Path.Combine(binDirectory, mode.ToString(), "Launcher.exe");
        var launcher = File.Exists(preferred)
            ? preferred
            : Directory.Exists(binDirectory)
                ? Directory.EnumerateFiles(binDirectory, "Launcher.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault()
                : null;

        return launcher is null
            ? Result<string>.Fail("The project build did not produce a Launcher executable.")
            : Result<string>.Ok(launcher);
    }

    private static bool IsEngineSourceDirectory(string path)
    {
        return File.Exists(Path.Combine(path, "CMakeLists.txt"))
            && Directory.Exists(Path.Combine(path, "engine"))
            && Directory.Exists(Path.Combine(path, "tools", "cmake"));
    }
}
