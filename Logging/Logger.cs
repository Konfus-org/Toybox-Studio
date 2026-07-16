using System.Runtime.CompilerServices;
using Toybox.Studio.Utils;
using TaskExtensions = Toybox.Studio.Utils.TaskExtensions;
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
public sealed class Logger : IDisposable
{
    private const string StudioCategory = "Studio";

    // The console scrollback lives here, not in its (fresh-per-open) view-model: every line flows through
    // Emit from the first startup line, so a console opened at any time replays the whole session. The
    // ring is bounded so a long-running or chatty session can't grow it without limit.
    private const int BacklogCapacity = 5000;

    private readonly LogFile _file;
    private readonly ILogTheme _theme;

    private readonly object _gate = new();
    private readonly List<LogEntry> _backlog = [];

    private Func<string, string, Task>? _engineForwarder;
    private Func<string, string, string, CancellationToken, Task>? _logColorSink;

    public Logger(LogFile file, ILogTheme theme)
    {
        _file = file;
        _theme = theme;
        // Re-push colors to the engine whenever the theme changes (a no-op while disconnected).
        _theme.Changed += PushLogColors;
        // Unobserved fire-and-forget failures (with no explicit handler) flow into the unified log, with the full
        // exception (type, message, stack) so a background failure can be traced to its source.
        TaskExtensions.SetDefaultErrorHandler(
            exception => Error($"Background task failed: {exception}"));
    }

    /// <summary>
    /// Raised for every log line, on the calling thread. Subscribers marshal as needed. A consumer that
    /// also needs the lines logged before it subscribed (the console) should use <see cref="Subscribe"/>,
    /// which hands back the current backlog and starts the subscription atomically.
    /// </summary>
    public event Action<LogEntry>? Logged;

    /// <summary>
    /// Starts <paramref name="handler"/> receiving future lines and returns the current backlog, both under
    /// one lock so no line is missed or delivered twice across the seam: a line logged concurrently either
    /// lands in the returned snapshot or is delivered to the handler, never both and never neither. The
    /// caller replays the snapshot, then relies on the event for new lines; unsubscribe with <c>Logged -=</c>.
    /// </summary>
    public IReadOnlyList<LogEntry> Subscribe(Action<LogEntry> handler)
    {
        lock (_gate)
        {
            Logged += handler;
            return _backlog.ToArray();
        }
    }

    /// <summary>
    /// Unsubscribes from the theme so this logger doesn't outlive its registration via the theme's event
    /// list. A no-op for the app's singleton logger, but keeps the wiring symmetric and safe if the logger
    /// ever becomes non-singleton.
    /// </summary>
    public void Dispose() => _theme.Changed -= PushLogColors;

    public void Info(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Emit(LogLevel.Info, message, file, line);

    public void Warning(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Emit(LogLevel.Warning, message, file, line);

    public void Error(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Emit(LogLevel.Error, message, file, line);

    public void Critical(string message, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) =>
        Emit(LogLevel.Critical, message, file, line);

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
        Emit(level, message, file, line);

    /// <summary>
    /// Logs a line from an arbitrary external source under its own category (e.g. "CMake"), with no source
    /// file. Generic by design — no per-source method or hard-coded category list.
    /// </summary>
    public void Log(LogLevel level, string category, string message) =>
        Emit(new LogEntry(level, $"[{category}] {message}"), forwardRaw: message);

    /// <summary>
    /// Surfaces a line as is. Useful for lines already tagged with a category, we use this for logs
    /// from the engine (engine.log). It is already tagged by the engine's core
    /// logger ("[Category][file:line] …"), so it is shown verbatim and never forwarded back.
    /// </summary>
    public void Log(LogLevel level, string message) =>
        Emit(new LogEntry(level, message), forwardRaw: null);

    /// <summary>
    /// Surfaces an already-tagged line (as <see cref="Log(LogLevel, string)"/>) that also carries its
    /// originating source location, so a console can link its "[file:line]" prefix to the real file.
    /// Used for engine lines, whose full source path rides alongside the message over the wire.
    /// </summary>
    public void Log(LogLevel level, string message, LogSource? source) =>
        Emit(new LogEntry(level, message, source), forwardRaw: null);

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

    private void Emit(LogLevel level, string message, string file, int line)
    {
        var name = Path.GetFileName(file);
        var composed = string.IsNullOrEmpty(name)
            ? $"[{StudioCategory}] {message}"
            : $"[{StudioCategory}][{name}:{line}] {message}";
        // The message only carries the bare file name, so ride the full caller path along on the entry: a
        // console can then link the "[name:line]" prefix to the real file. Forward the raw message; the
        // engine re-tags it under its own "[Studio]" scope.
        var source = string.IsNullOrEmpty(file) ? (LogSource?)null : new LogSource(file, line);
        Emit(new LogEntry(level, composed, source), forwardRaw: message);
    }

    private void Emit(LogEntry entry, string? forwardRaw)
    {
        _file.Write(entry);

        // Append to the backlog and capture the subscriber list under the same lock Subscribe takes, so the
        // snapshot-and-subscribe seam stays exactly-once (see Subscribe). Invoke outside the lock — a
        // subscriber must never run while we hold it.
        Action<LogEntry>? handlers;
        lock (_gate)
        {
            _backlog.Add(entry);
            if (_backlog.Count > BacklogCapacity)
                _backlog.RemoveRange(0, _backlog.Count - BacklogCapacity);
            handlers = Logged;
        }
        handlers?.Invoke(entry);

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

        // The active theme is UI-thread state, but PushLogColors is also called off-thread (from the
        // session's RPC connect via SetLogColorSink). Snapshot the colours on the UI thread, then fire the
        // (already off-thread-safe) push so we never read a theme mid-swap from a background thread.
        Dispatch.To(DispatchContext.UI, () =>
        {
            // The engine console takes a single hex per level; the theme source supplies each already
            // collapsed to its representative hex.
            var (info, warning, error) = _theme.Colors;
            PushLogColorsSafelyAsync(sink, info, warning, error).FireAndForget();
        });
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
