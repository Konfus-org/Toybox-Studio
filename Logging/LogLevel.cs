namespace Toybox.Studio.Logging;

/// <summary>
/// Severity of a log line, mirroring the engine's <c>tbx::LogLevel</c> (INFO, WARNING, ERROR, CRITICAL)
/// in order. The RPC wire form is the lower-case name.
/// </summary>
public enum LogLevel
{
    Info,
    Warning,
    Error,
    Critical,
}

public static class LogLevels
{
    /// <summary>Parses the engine/RPC wire name (case-insensitive); anything unknown reads as Info.</summary>
    public static LogLevel Parse(string? level) => level?.Trim().ToLowerInvariant() switch
    {
        "warning" or "warn" => LogLevel.Warning,
        "error" => LogLevel.Error,
        "critical" or "fatal" => LogLevel.Critical,
        _ => LogLevel.Info,
    };

    /// <summary>The lower-case wire name the engine expects (info/warning/error/critical).</summary>
    public static string ToWire(this LogLevel level) => level switch
    {
        LogLevel.Warning => "warning",
        LogLevel.Error => "error",
        LogLevel.Critical => "critical",
        _ => "info",
    };
}
