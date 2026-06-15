using System.IO;
using System.Runtime.CompilerServices;
using Toybox.Studio.Theming;
namespace Toybox.Studio.Logging;

/// <summary>
/// The single entry point for all editor-side logging. Every line — whether emitted by the studio or
/// streamed from the engine — flows through here, which: fans it out to subscribers (the console),
/// persists it to the log file, and, for editor-originated lines, forwards them into the engine's normal
/// Toybox logging (so editor and engine logs share one unified log). It also keeps the engine console's
/// colors in sync with the editor theme as a private detail of logging.
///
/// Categories are generic: studio lines are tagged "[Studio][file.cs:line]"; any other source supplies its
/// own category name via <see cref="External"/> (e.g. "CMake"); engine lines arrive already tagged by the
/// engine's core logger (which prefixes the active app/plugin category) and are shown verbatim.
/// </summary>
public sealed class Logger
{
    private const string StudioCategory = "Studio";

    private readonly LogFile _file;
    private readonly ThemeManager _theme;

    private Func<string, string, Task>? _engineForwarder;
    private Func<string, string, string, CancellationToken, Task>? _logColorSink;

    public Logger(LogFile file, ThemeManager theme)
    {
        _file = file;
        _theme = theme;
        // Re-push colors to the engine whenever the theme changes (a no-op while disconnected).
        _theme.ThemeChanged += PushLogColors;
        // Unobserved fire-and-forget failures (with no explicit handler) flow into the unified log.
        TaskExtensions.SetDefaultErrorHandler(
            exception => Error($"Background task failed: {exception.GetType().Name}: {exception.Message}"));
    }

    /// <summary>
    /// Raised for every log line, on the calling thread. Subscribers marshal as needed.
    /// </summary>
    public event Action<LogEntry>? Logged;

    public void Info(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Studio(LogLevel.Info, message, file, line);

    public void Warning(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Studio(LogLevel.Warning, message, file, line);

    public void Error(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Studio(LogLevel.Error, message, file, line);

    public void Critical(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Studio(LogLevel.Critical, message, file, line);

    /// <summary>
    /// Logs a studio-originated line at a level chosen at runtime. Prefer <see cref="Info"/>/
    /// <see cref="Warning"/>/<see cref="Error"/>/<see cref="Critical"/> for fixed levels; this overload is
    /// for cases where the level is computed (e.g. success-vs-failure). Tagged "[Studio]" with the calling
    /// source file.
    /// </summary>
    public void Log(
        LogLevel level,
        string message,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0) =>
        Studio(level, message, file, line);

    /// <summary>
    /// Logs a line from an arbitrary external source under its own category (e.g. "CMake"), with no source
    /// file. Generic by design — no per-source method or hard-coded category list.
    /// </summary>
    public void External(LogLevel level, string category, string message) =>
        Emit(new LogEntry(level, $"[{category}] {message}"), forwardRaw: message);

    /// <summary>
    /// Surfaces a line streamed from the engine (engine.log). It is already tagged by the engine's core
    /// logger ("[Category][file:line] …"), so it is shown verbatim and never forwarded back.
    /// </summary>
    public void IngestEngine(LogLevel level, string message) =>
        Emit(new LogEntry(level, message), forwardRaw: null);

    /// <summary>
    /// Sets (or clears) the sink that pushes editor lines into the engine's unified log. The session wires
    /// this to the live RPC client on connect and clears it on disconnect.
    /// </summary>
    public void SetEngineForwarder(Func<string, string, Task>? forwarder) => _engineForwarder = forwarder;

    /// <summary>
    /// Sets (or clears) the sink that pushes the editor theme's log colors to the engine console. The
    /// session wires this to the live RPC client on connect (colors are pushed immediately) and clears it
    /// on disconnect.
    /// </summary>
    public void SetLogColorSink(Func<string, string, string, CancellationToken, Task>? sink)
    {
        _logColorSink = sink;
        if (sink is not null)
            PushLogColors();
    }

    private void Studio(LogLevel level, string message, string file, int line)
    {
        var name = Path.GetFileName(file);
        var composed = string.IsNullOrEmpty(name)
            ? $"[{StudioCategory}] {message}"
            : $"[{StudioCategory}][{name}:{line}] {message}";
        // Forward the raw message; the engine re-tags it under its own "[Studio]" scope.
        Emit(new LogEntry(level, composed), forwardRaw: message);
    }

    private void Emit(LogEntry entry, string? forwardRaw)
    {
        _file.Write(entry);
        Logged?.Invoke(entry);

        if (forwardRaw is not null)
            ForwardToEngine(entry.Level, forwardRaw);
    }

    private void ForwardToEngine(LogLevel level, string rawMessage)
    {
        var forwarder = _engineForwarder;
        if (forwarder is null)
            return;

        // Fire-and-forget: logging must never block, and a disconnect race is harmless.
        ForwardSafelyAsync(forwarder, level.ToWire(), rawMessage).FireAndForget();
    }

    private static async Task ForwardSafelyAsync(Func<string, string, Task> forwarder, string level, string message)
    {
        try
        {
            await forwarder(level, message).ContinueOnAnyContext();
        }
        catch (Exception)
        {
            // Best-effort; the line still lives in the editor console and the owned-session file.
        }
    }

    private void PushLogColors()
    {
        var sink = _logColorSink;
        if (sink is null)
            return;

        var colors = _theme.Active.Colors;
        PushLogColorsSafelyAsync(sink, colors.Info, colors.Warning, colors.Error).FireAndForget();
    }

    private static async Task PushLogColorsSafelyAsync(
        Func<string, string, string, CancellationToken, Task> sink,
        string info,
        string warning,
        string error)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await sink(info, warning, error, cts.Token).ContinueOnAnyContext();
        }
        catch (Exception)
        {
            // Best-effort cosmetic sync; a disconnect or older engine simply ignores it.
        }
    }
}
