using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Projects;

/// <summary>
/// Builds the engine checkout itself, standalone, in a given <see cref="BuildMode"/>: configures (once)
/// and compiles the engine's own CMake tree through the shared <see cref="BuildRunner"/>, via the
/// engine's presets, which put each toolchain's tree in &lt;engine&gt;/build/&lt;preset&gt;. Distinct
/// from <see cref="ProjectBuilder"/>, which builds the engine in-tree with a project — this is for
/// working on the engine itself (Build ▸ Engine), where the engine's tests and tools matter and no
/// project output is wanted. The checkout is located the same way the project build locates it (the
/// configured settings path, or beside the project).
/// </summary>
public sealed class EngineBuilder
{
    private readonly EngineSourceLocator _locator;
    private readonly BuildRunner _runner;
    private readonly Logger _log;

    public EngineBuilder(EngineSourceLocator locator, BuildRunner runner, Logger log)
    {
        _locator = locator;
        _runner = runner;
        _log = log;
    }

    /// <summary>
    /// Configures (once) and builds the engine checkout the active project compiles against, via the
    /// engine's own CMake presets in the given mode.
    /// </summary>
    public async Task<Result> BuildAsync(Project project, BuildMode mode, CancellationToken ct)
    {
        var located = _locator.Locate(project.Path);
        if (!located)
            return Result.Fail(located.Error!);

        var engineSource = located.Value!;
        _log.Info($"Compiling the engine at {engineSource}...");

        // The engine's presets put each toolchain's tree in build/<preset> (unlike a project's single
        // build/), so the runner probes and configures per preset.
        var built = await _runner
            .RunAsync(
                engineSource,
                configurePreset => Path.Combine(engineSource, "build", configurePreset),
                new Dictionary<string, string>(),
                mode,
                ct)
            .ContinueOnAnyContext();
        if (!built)
            return built;

        _log.Info("Engine compiled.");
        return Result.Ok();
    }
}
