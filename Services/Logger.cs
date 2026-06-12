namespace Toybox.Studio.Services;

/// <summary>
/// The single entry point for all editor-side logging. Every line — whether emitted by the studio or
/// streamed from the engine — flows through here, which: fans it out to subscribers (the console),
/// persists it to the log file, and, for studio-originated lines, forwards them into the engine's
/// normal Toybox logging (so editor and engine logs share one unified log). It also keeps the engine
/// console's colors in sync with the editor theme as a private detail of logging.
/// </summary>
public sealed class Logger
{
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

    public void Info(string message) => Log("info", message);

    public void Warning(string message) => Log("warning", message);

    public void Error(string message) => Log("error", message);

    public void Critical(string message) => Log("critical", message);

    /// <summary>
    /// Logs a studio-originated line. Matches the (level, message) shape build callbacks expect.
    /// </summary>
    public void Log(string level, string message) => Emit(new LogEntry(level, message, Source: "studio"));

    /// <summary>
    /// Surfaces a line streamed from the engine (engine.log). Never forwarded back.
    /// </summary>
    public void IngestEngineLog(string level, string message) =>
        Emit(new LogEntry(level, message, Source: "engine"));

    /// <summary>
    /// Sets (or clears) the sink that pushes studio lines into the engine's unified log. The session
    /// wires this to the live RPC client on connect and clears it on disconnect.
    /// </summary>
    public void SetEngineForwarder(Func<string, string, Task>? forwarder) => _engineForwarder = forwarder;

    /// <summary>
    /// Sets (or clears) the sink that pushes the editor theme's log colors to the engine console. The
    /// session wires this to the live RPC client on connect (colors are pushed immediately) and clears
    /// it on disconnect.
    /// </summary>
    public void SetLogColorSink(Func<string, string, string, CancellationToken, Task>? sink)
    {
        _logColorSink = sink;
        if (sink is not null)
            PushLogColors();
    }

    private void Emit(LogEntry entry)
    {
        _file.Write(entry);
        Logged?.Invoke(entry);

        if (entry.IsStudio)
            ForwardToEngine(entry);
    }

    private void ForwardToEngine(LogEntry entry)
    {
        var forwarder = _engineForwarder;
        if (forwarder is null)
            return;

        // Fire-and-forget: logging must never block, and a disconnect race is harmless.
        ForwardSafelyAsync(forwarder, entry.Level, entry.Message).FireAndForget();
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
