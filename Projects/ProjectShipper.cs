using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Projects;

/// <summary>
/// Ships a project: builds it in the target <see cref="BuildMode"/> through the
/// <see cref="ProjectBuilder"/>, then copies the build's whole output folder — the launcher and
/// everything beside it — into a target directory, so what lands there is the runnable game exactly
/// as the build produced it. Failures (the build's, or the copy's) come back as a failed
/// <see cref="Result{T}"/> for the caller to report.
/// </summary>
public sealed class ProjectShipper(ProjectBuilder builder, Logger log)
{
    /// <summary>Builds and ships the project; returns the shipped launcher's path.</summary>
    public async Task<Result<string>> ShipAsync(
        Project project, string targetDirectory, BuildMode mode, CancellationToken ct)
    {
        var build = await builder.BuildAsync(project, mode, ct).ContinueOnAnyContext();
        if (!build)
            return build;

        var outputDirectory = Path.GetDirectoryName(build.Value!)!;
        log.Info($"Shipping '{project.Name}' ({mode}) to {targetDirectory}...");
        try
        {
            // The output can be big (engine binaries, assets), so the copy runs off the UI thread.
            await Task.Run(() => Copy(outputDirectory, targetDirectory, ct), ct).ContinueOnAnyContext();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Result<string>.Fail($"Shipping '{project.Name}' failed: {exception.Message}");
        }

        log.Info($"Shipped '{project.Name}'.");
        return Result<string>.Ok(Path.Combine(targetDirectory, Path.GetFileName(build.Value!)));
    }

    private static void Copy(string source, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }
}
