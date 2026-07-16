namespace Toybox.Studio.Logging;

/// <summary>
/// A single log line ready for display: a severity and a fully-composed message. The category and source
/// location (e.g. <c>[Studio][file.cs:42]</c>) are already prefixed into <see cref="Message"/> by whoever
/// produced it — the engine's core logger for engine lines, the <see cref="Logger"/> for editor lines.
/// <see cref="Source"/> carries the originating file's full path (editor lines only) so a console can link
/// the message's <c>file:line</c> prefix to the real file; it is null for engine lines, whose file survives
/// only as the name baked into <see cref="Message"/>.
/// </summary>
public sealed record LogEntry(LogLevel Level, string Message, LogSource? Source = null)
{
    public bool IsError => Level is LogLevel.Error or LogLevel.Critical;

    public bool IsWarning => Level == LogLevel.Warning;
}
