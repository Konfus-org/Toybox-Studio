using System.Text.RegularExpressions;
using Toybox.Studio.CMake;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Git;

/// <summary>
/// Runs the git command-line for the editor's source-control needs — cloning a checkout when one isn't
/// found (the engine source, first of all) and fast-forward-pulling updates into an existing one. A thin
/// wrapper over the shared <see cref="CommandRunner"/> (git is never handed a shell string), returning a
/// <see cref="Result"/> the caller reports rather than throwing; git's own progress and errors stream
/// into the unified log. Each operation optionally reports its transfer progress to a caller-supplied
/// <see cref="IProgress{T}"/> (the fraction of objects received, in [0,1]) — the caller decides where that
/// goes (the launch flow drives the splash's activity bar with it). Requires git on PATH —
/// <see cref="IsInstalledAsync"/> checks up front so a missing git gives a clear message instead of a
/// launch failure mid-operation.
/// </summary>
public sealed partial class GitClient(CommandRunner runner, Logger log)
{
    private const string Executable = "git";

    private const string MissingGit =
        "Git isn't installed, or isn't on PATH. Install Git, then try again.";

    // git's dominant clone/pull phase redraws "Receiving objects:  NN% (n/m), …" in place (a bare carriage
    // return between updates — see CommandRunner.PumpAsync). Its percentage is the download's progress; the
    // quick phases around it (counting, resolving deltas) report none and leave the bar indeterminate.
    [GeneratedRegex(@"Receiving objects:\s+(?<percent>\d+)%", RegexOptions.Compiled)]
    private static partial Regex ReceivingObjectsPattern();

    /// <summary>True when a git executable is on PATH (runs <c>git --version</c>).</summary>
    public async Task<bool> IsInstalledAsync(CancellationToken ct = default) =>
        await runner.RunAsync(Executable, ["--version"], ct: ct).ContinueOnAnyContext();

    /// <summary>
    /// Clones <paramref name="repoUrl"/> into <paramref name="targetDirectory"/> — which must not already
    /// exist as a non-empty folder, since git creates it. Reports the transfer fraction to
    /// <paramref name="progress"/> as it downloads (and streams git's output to the log). Fails (never
    /// throws) on a missing git, a non-empty target, or a git error.
    /// </summary>
    public async Task<Result> CloneAsync(
        string repoUrl,
        string targetDirectory,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (Directory.Exists(targetDirectory)
            && Directory.EnumerateFileSystemEntries(targetDirectory).Any())
        {
            return Result.Fail(
                $"Can't clone into '{targetDirectory}' — the folder already exists and isn't empty.");
        }

        if (!await IsInstalledAsync(ct).ContinueOnAnyContext())
            return Result.Fail(MissingGit);

        // git creates the leaf itself, but not its parents; make sure they exist so the clone lands.
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(targetDirectory));
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        log.Info($"Cloning '{repoUrl}' into '{targetDirectory}'...");
        var cloned = await RunGitAsync(
            ["clone", "--progress", repoUrl, targetDirectory],
            workingDirectory: null,
            progress,
            ct).ContinueOnAnyContext();
        return cloned ? Result.Ok() : Result.Fail($"Cloning '{repoUrl}' failed: {cloned.Error}");
    }

    /// <summary>
    /// Fast-forward-pulls the checkout at <paramref name="directory"/> (a diverged branch is left
    /// untouched rather than merged), reporting any transfer fraction to <paramref name="progress"/>.
    /// Fails on a missing git or a git error.
    /// </summary>
    public async Task<Result> PullAsync(
        string directory,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!await IsInstalledAsync(ct).ContinueOnAnyContext())
            return Result.Fail(MissingGit);

        log.Info($"Pulling updates in '{directory}'...");
        var pulled = await RunGitAsync(
            ["pull", "--ff-only", "--progress"],
            workingDirectory: directory,
            progress,
            ct).ContinueOnAnyContext();
        return pulled ? Result.Ok() : Result.Fail($"Pulling '{directory}' failed: {pulled.Error}");
    }

    /// <summary>
    /// Runs a git subcommand through the shared runner, logging its output and mapping git's
    /// "Receiving objects" percentage to <paramref name="progress"/> (the receive phase is the bulk of a
    /// transfer; the quick phases around it report nothing and leave a progress bar indeterminate).
    /// </summary>
    private Task<Result<int>> RunGitAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IProgress<double>? progress,
        CancellationToken ct) =>
        runner.RunAsync(
            Executable,
            arguments,
            workingDirectory: workingDirectory,
            logOutput: true,
            progress: progress,
            progressOf: progress is null ? null : ParseReceivingProgress,
            ct: ct);

    // The transfer's completion fraction from a git "Receiving objects: NN%" line, or null for any other
    // line (so the bar only advances on the receive phase and stays put through the quick phases around it).
    private static double? ParseReceivingProgress(string line)
    {
        var match = ReceivingObjectsPattern().Match(line);
        return match.Success && int.TryParse(match.Groups["percent"].ValueSpan, out var percent)
            ? Math.Clamp(percent / 100.0, 0, 1)
            : null;
    }
}
