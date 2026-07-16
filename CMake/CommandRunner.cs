using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.CMake;

/// <summary>
/// Runs external command-line tools (cmake, dev tools, …) the same way everywhere: an executable plus an
/// argument list, never a shell string, so there is no quoting/escaping hazard. Runs to completion and
/// returns the exit code as the result's value (success is exit code 0). Output is discarded by default;
/// when <c>logOutput</c> is set it is streamed to the unified log, each line categorized by level via
/// <c>logCategoryRegex</c>. A caller that wants a live progress bar supplies <c>progressOf</c> (which maps
/// an output line to a fraction in [0,1], or null for lines that carry no progress) and a
/// <c>progress</c> sink to report those fractions to. Supports a timeout and cooperative cancellation;
/// either kills the process tree. The one place the editor shells out.
/// </summary>
public sealed class CommandRunner(Logger log)
{
    // Categorizes a logged output line by the first named group that matches. A caller supplies its own
    // pattern (e.g. the cmake build's error/warning shapes); the default just looks for the level words.
    private static readonly Regex DefaultLogCategoryRegex = new(
        @"(?<critical>critical|fatal)|(?<error>error)|(?<warning>warning|warn)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        IProgress<double>? progress = null,
        Func<string, double?>? progressOf = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        using var process = Build(command, arguments, workingDirectory);
        if (!TryStart(process, command, arguments, logOutput, out var failure))
            return failure;

        // Read both streams concurrently — so neither can fill its pipe and stall the child — through one
        // line handler that logs and/or reports progress per line.
        var onLine = LineHandler(command, logOutput, logCategoryRegex, progress, progressOf);
        var pumpOut = PumpAsync(process.StandardOutput, onLine);
        var pumpError = PumpAsync(process.StandardError, onLine);

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

            // The kill closes the streams, so the pumps end on their own; wait them out so no output
            // handler runs after this returns.
            await Task.WhenAll(pumpOut, pumpError).ContinueOnAnyContext();

            // A caller-requested cancel propagates; a timeout is reported as a failed result.
            if (ct.IsCancellationRequested)
                throw;

            return TimedOut(command, timeout!.Value);
        }

        // Drain the streams fully (they close as the process exits) before reporting the exit code.
        await Task.WhenAll(pumpOut, pumpError).ContinueOnAnyContext();
        return FromExit(command, process.ExitCode);
    }

    private static Process Build(
        string fileName,
        IReadOnlyList<string>? arguments,
        string? workingDirectory)
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

        return new Process { StartInfo = startInfo, EnableRaisingEvents = true };
    }

    // The per-line action the stream pumps call: logs the line (categorized by level) when asked, and
    // maps it to a progress fraction when a parser is supplied. Both pumps share the one action and may
    // call it concurrently — it only touches the logger and the progress sink, both of which are
    // already used from multiple threads.
    private Action<string> LineHandler(
        string fileName,
        bool logOutput,
        Regex? logCategoryRegex,
        IProgress<double>? progress,
        Func<string, double?>? progressOf)
    {
        var category = Path.GetFileNameWithoutExtension(fileName);
        var pattern = logCategoryRegex ?? DefaultLogCategoryRegex;
        return line =>
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            if (logOutput)
                log.Log(Classify(pattern, line), category, line);
            if (progress is not null && progressOf?.Invoke(line) is { } fraction)
                progress.Report(fraction);
        };
    }

    private bool TryStart(
        Process process,
        string fileName,
        IReadOnlyList<string>? arguments,
        bool logOutput,
        out Result<int> failure)
    {
        if (logOutput)
            log.Info(Describe(fileName, arguments));

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

        failure = default;
        return true;
    }

    // Reads a redirected stream to end, handing each line to <paramref name="onLine"/>. Splits on a bare
    // carriage return as well as a line feed (and treats CRLF as one break via the empty-segment skip):
    // tools that redraw a status line in place with a lone '\r' — git's "Receiving objects: NN%" — surface
    // each update live this way, whereas the framework's line reader would buffer them until the phase's
    // closing newline. Reading both streams on their own pump keeps a full pipe from stalling the child.
    private static async Task PumpAsync(TextReader reader, Action<string> onLine)
    {
        var buffer = new char[4096];
        var segment = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            for (var i = 0; i < count; i++)
            {
                var c = buffer[i];
                if (c is '\n' or '\r')
                {
                    if (segment.Length > 0)
                    {
                        onLine(segment.ToString());
                        segment.Clear();
                    }
                }
                else
                {
                    segment.Append(c);
                }
            }
        }

        if (segment.Length > 0)
            onLine(segment.ToString());
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
