using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Projects;

/// <summary>
/// Builds a project in a given <see cref="BuildMode"/>: configures (once) and compiles its CMake tree
/// through the shared <see cref="BuildRunner"/> — against the engine checkout from the editor settings,
/// or the one found beside the project — and returns the launcher the build produced. By convention the
/// CMake target (and so the app module) is named after the project's root folder, and the build tree
/// lives in &lt;root&gt;/build. Failures come back as a failed <see cref="Result{T}"/> for the caller to
/// report; progress is narrated as studio log lines.
/// </summary>
public sealed class ProjectBuilder
{
    private readonly EngineSourceLocator _engineLocator;
    private readonly BuildRunner _runner;
    private readonly Logger _log;

    public ProjectBuilder(EngineSourceLocator engineLocator, BuildRunner runner, Logger log)
    {
        _engineLocator = engineLocator;
        _runner = runner;
        _log = log;
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

        var located = _engineLocator.Locate(project.Path);
        if (!located)
            return Result<string>.Fail(located.Error!);

        var engineSource = located.Value!;
        var buildDirectory = Path.Combine(project.Path, "build");
        _log.Info($"Compiling project '{project.Name}' against the engine at {engineSource}...");

        var defines = new Dictionary<string, string>
        {
            ["TBX_ENGINE_DIR"] = engineSource.Replace('\\', '/'),
        };
        var built = await _runner
            .RunAsync(project.Path, _ => buildDirectory, defines, mode, ct).ContinueOnAnyContext();
        if (!built)
            return Result<string>.Fail(built.Error!);

        _log.Info($"Project '{project.Name}' compiled.");
        return FindLauncher(buildDirectory, mode);
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
}
