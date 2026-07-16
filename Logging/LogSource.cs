namespace Toybox.Studio.Logging;

/// <summary>
/// The originating source location of a log line: the full path to the file that logged it and the line
/// number within it. Carried alongside the (already source-tagged) <see cref="LogEntry.Message"/> so a
/// console can turn the <c>file:line</c> prefix into a link that opens the real file — the message text
/// itself only ever shows the bare file name, so the full path has to travel out of band.
/// Editor-originated lines fill this in from the caller's <c>[CallerFilePath]</c>; engine lines leave it
/// null (their file exists only as a name in the message, resolved best-effort by the console instead).
/// </summary>
public readonly record struct LogSource(string File, int Line);
