using Toybox.Studio.Logging;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Projects;

/// <summary>
/// Locates the engine source checkout the native builds compile against: the configured settings path
/// when it points at a real engine, otherwise found by climbing a project's ancestor folders for an
/// Engine checkout (a project lives beside the engine in a Toybox root, e.g.
/// &lt;Toybox&gt;/ExampleProject beside &lt;Toybox&gt;/Engine). The settings path is read live on every
/// lookup, so fixing it in the Settings window applies to the next build without a restart. Shared by
/// <see cref="ProjectBuilder"/> (which builds the engine in-tree with the project) and
/// <see cref="EngineBuilder"/> (which builds the checkout itself).
/// </summary>
public sealed class EngineSourceLocator(SettingsManager settings, Logger log)
{
    /// <summary>The engine source tree for a project at <paramref name="projectRoot"/>, or a failure
    /// telling the user where to point the editor when neither the settings nor the project's
    /// surroundings yield one.</summary>
    public Result<string> Locate(string projectRoot) =>
        Find(projectRoot) is { } engineSource
            ? Result<string>.Ok(engineSource)
            : Result<string>.Fail(
                "No Toybox engine found: set the engine source path in the editor settings, or place "
                    + $"the project beside an Engine checkout (in one of '{projectRoot}'s ancestor folders).");

    private string? Find(string projectRoot)
    {
        var configured = settings.Editor.Engine.SourcePath;
        if (configured.Length > 0)
        {
            if (IsEngineSourceDirectory(configured))
                return configured;

            log.Warning(
                $"The configured engine source path '{configured}' is not an engine "
                    + "checkout; searching beside the project instead.");
        }

        for (var directory = new DirectoryInfo(projectRoot).Parent;
            directory is not null;
            directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Engine");
            if (IsEngineSourceDirectory(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsEngineSourceDirectory(string path)
    {
        return File.Exists(Path.Combine(path, "CMakeLists.txt"))
            && Directory.Exists(Path.Combine(path, "engine"))
            && Directory.Exists(Path.Combine(path, "tools", "cmake"));
    }
}
