using Toybox.Studio.Utils;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Toybox.Studio.Services.Logging;

namespace Toybox.Studio.Services;

/// <summary>
/// Runs external command-line tools (cmake, dev tools, …) the same way everywhere: an executable plus an
/// argument list, never a shell string, so there is no quoting/escaping hazard. Runs to completion and
/// returns the exit code as the result's value (success is exit code 0). Output is discarded by default;
/// when <c>logOutput</c> is set it is streamed to the unified log, each line categorized by level via
/// <c>logCategoryRegex</c>. Supports a timeout and (async only) cooperative cancellation; either kills the
/// process tree. <see cref="Run"/> and <see cref="RunAsync"/> share one signature bar the async suffix and
/// cancellation token. The one place the editor shells out.
/// </summary>
public sealed class CommandRunner
{
    private readonly Logger _log;

    // Categorizes a logged output line by the first named group that matches. A caller supplies its own
    // pattern (e.g. the cmake build's error/warning shapes); the default just looks for the level words.
    private static readonly Regex DefaultLogCategoryRegex = new(
        @"(?<critical>critical|fatal)|(?<error>error)|(?<warning>warning|warn)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public CommandRunner(Logger log)
    {
        _log = log;
    }

    /// <summary>
    /// Runs <paramref name="command"/> with <paramref name="arguments"/> to completion. The result's value
    /// is the process exit code and success means it exited 0; a launch failure or timeout is a failed
    /// result with an explanatory message and value -1 ("did not run"). See the type summary for
    /// output/logging behavior.
    /// </summary>
    public async Task<Result<int>> RunAsync(
        string command,
        IReadOnlyList<string>? arguments = null,
        string? workingDirectory = null,
        bool logOutput = false,
        Regex? logCategoryRegex = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        using var process = Build(command, arguments, workingDirectory, logOutput, logCategoryRegex);
        if (!TryStart(process, command, arguments, logOutput, out var failure))
            return failure;

        using var timeoutSource = timeout is { } span ? new CancellationTokenSource(span) : null;
        using var linked = timeoutSource is null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ContinueOnAnyContext();
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            // A caller-requested cancel propagates; a timeout is reported as a failed result.
            if (ct.IsCancellationRequested)
                throw;

            return TimedOut(command, timeout!.Value);
        }

        process.WaitForExit(); // Drain the async output handlers before reporting.
        return FromExit(command, process.ExitCode);
    }

    /// <summary>
    /// The synchronous form of <see cref="RunAsync"/>; identical behavior and signature minus cancellation.
    /// Blocks the calling thread until the process exits, so prefer <see cref="RunAsync"/> off the UI thread.
    /// </summary>
    public Result<int> Run(
        string command,
        IReadOnlyList<string>? arguments = null,
        string? workingDirectory = null,
        bool logOutput = false,
        Regex? logCategoryRegex = null,
        TimeSpan? timeout = null)
    {
        using var process = Build(command, arguments, workingDirectory, logOutput, logCategoryRegex);
        if (!TryStart(process, command, arguments, logOutput, out var failure))
            return failure;

        if (timeout is { } span && !process.WaitForExit((int)span.TotalMilliseconds))
        {
            TryKill(process);
            return TimedOut(command, span);
        }

        process.WaitForExit(); // Waits out a no-timeout run and drains the async output handlers.
        return FromExit(command, process.ExitCode);
    }

    private Process Build(
        string fileName,
        IReadOnlyList<string>? arguments,
        string? workingDirectory,
        bool logOutput,
        Regex? logCategoryRegex)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (!string.IsNullOrEmpty(workingDirectory))
            startInfo.WorkingDirectory = workingDirectory;
        if (arguments is not null)
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        // Without logging we still read (and discard) the streams so the child never blocks on a full pipe;
        // BeginOutputReadLine drains them whether or not anyone is subscribed.
        if (logOutput)
        {
            var category = Path.GetFileNameWithoutExtension(fileName);
            var pattern = logCategoryRegex ?? DefaultLogCategoryRegex;
            void Forward(string? line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    _log.Log(Classify(pattern, line), category, line);
            }

            process.OutputDataReceived += (_, e) => Forward(e.Data);
            process.ErrorDataReceived += (_, e) => Forward(e.Data);
        }

        return process;
    }

    private bool TryStart(
        Process process,
        string fileName,
        IReadOnlyList<string>? arguments,
        bool logOutput,
        out Result<int> failure)
    {
        if (logOutput)
            _log.Info(Describe(fileName, arguments));

        try
        {
            if (!process.Start())
            {
                failure = NotRun($"Failed to start '{fileName}'.");
                return false;
            }
        }
        catch (Exception exception)
        {
            failure = NotRun($"Failed to start '{fileName}': {exception.Message}");
            return false;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        failure = default;
        return true;
    }

    private static LogLevel Classify(Regex pattern, string line)
    {
        var match = pattern.Match(line);
        if (!match.Success)
            return LogLevel.Info;
        if (match.Groups["critical"].Success)
            return LogLevel.Critical;
        if (match.Groups["error"].Success)
            return LogLevel.Error;
        if (match.Groups["warning"].Success)
            return LogLevel.Warning;
        return LogLevel.Info;
    }

    private static Result<int> FromExit(string fileName, int exitCode) =>
        exitCode == 0
            ? Result<int>.Ok(0)
            : new Result<int>(false, exitCode, $"'{fileName}' exited with code {exitCode}.");

    private static Result<int> TimedOut(string fileName, TimeSpan timeout) =>
        NotRun($"'{fileName}' timed out after {timeout.TotalSeconds:0.#}s.");

    // A failure where the process never produced an exit code (could not launch, or was killed on
    // timeout). The value is -1 to mark "did not run", distinct from any real (non-negative) exit code.
    private static Result<int> NotRun(string error) => new(false, -1, error);

    private static string Describe(string fileName, IReadOnlyList<string>? arguments) =>
        arguments is { Count: > 0 }
            ? $"Running: {fileName} {string.Join(' ', arguments)}"
            : $"Running: {fileName}";

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Already exited.
        }
    }
}
