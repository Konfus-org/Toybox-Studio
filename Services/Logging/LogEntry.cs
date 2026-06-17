namespace Toybox.Studio.Services.Logging;

/// <summary>
/// A single log line ready for display: a severity and a fully-composed message. The category and source
/// location (e.g. <c>[Studio][file.cs:42]</c>) are already prefixed into <see cref="Message"/> by whoever
/// produced it — the engine's core logger for engine lines, the <see cref="Logger"/> for editor lines.
/// </summary>
public sealed record LogEntry(LogLevel Level, string Message)
{
    public bool IsError => Level is LogLevel.Error or LogLevel.Critical;

    public bool IsWarning => Level == LogLevel.Warning;
}
