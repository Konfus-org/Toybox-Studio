namespace Toybox.Studio.Services;

/// <summary>
/// A single log line, either streamed from the engine or emitted by the studio itself.
/// </summary>
public sealed record LogEntry(string Level, string Message, string Source = "engine")
{
    public bool IsError => Level is "error" or "critical";

    public bool IsWarning => Level == "warning";

    public bool IsStudio => Source == "studio";
}
