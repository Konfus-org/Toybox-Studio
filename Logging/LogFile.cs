using Toybox.Studio.Project;
namespace Toybox.Studio.Logging;

/// <summary>
/// Persists the editor's unified log stream to a single rotated file, ~/.toybox/Logs/TbxStudio.log.
/// Every studio line and every engine line streamed back over RPC flows through here. On startup the
/// previous runs are rotated aside (TbxStudio.log → TbxStudio_1.log → … → TbxStudio_10.log), matching
/// the core engine's own log rotation, and a fresh file is opened for this run.
/// </summary>
public sealed class LogFile : IDisposable
{
    /// <summary>
    /// The ~/.toybox/Logs folder that holds the editor's TbxStudio.log files.
    /// </summary>
    public static readonly string LogsDirectory =
        Path.Combine(Settings.BaseDirectory, "Logs");

    private const string BaseName = "TbxStudio";
    private const string Extension = ".log";
    private const int MaxHistory = 10;

    private readonly object _sync = new();
    private StreamWriter? _writer;

    /// <summary>
    /// The file currently being written (this run's TbxStudio.log).
    /// </summary>
    public string? CurrentFilePath { get; private set; }

    public LogFile()
    {
        OpenFreshLog();
    }

    /// <summary>
    /// Appends one entry to the current log file.
    /// </summary>
    public void Write(LogEntry entry)
    {
        lock (_sync)
        {
            if (_writer is null)
                return;

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            _writer.WriteLine($"[{timestamp}] [{entry.Level}] [{entry.Source}] {entry.Message}");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            CloseWriter();
            CurrentFilePath = null;
        }
    }

    /// <summary>
    /// Rotates the previous runs aside and opens a fresh TbxStudio.log for this run.
    /// </summary>
    private void OpenFreshLog()
    {
        lock (_sync)
        {
            CloseWriter();
            var path = Rotate(LogsDirectory, BaseName, Extension, MaxHistory);
            CurrentFilePath = path;
            _writer = new StreamWriter(path, append: false) { AutoFlush = true };
        }
    }

    /// <summary>
    /// Rotates the run history in <paramref name="directory"/>, mirroring the engine's FileOperator:
    /// from the oldest slot down, move <c>{base}_{i-1}{ext}</c> to <c>{base}_{i}{ext}</c> (slot 0 is
    /// the unsuffixed <c>{base}{ext}</c>), dropping anything past <paramref name="maxHistory"/>. Returns
    /// the path for this run (slot 0), which is now free to (re)create.
    /// </summary>
    private static string Rotate(string directory, string baseName, string extension, int maxHistory)
    {
        Directory.CreateDirectory(directory);

        string SlotPath(int index)
        {
            var suffix = index <= 0 ? "" : $"_{index}";
            return Path.Combine(directory, $"{baseName}{suffix}{extension}");
        }

        if (maxHistory < 1)
            return SlotPath(0);

        for (var index = maxHistory; index >= 1; index--)
        {
            var from = SlotPath(index - 1);
            var to = SlotPath(index);
            if (!File.Exists(from))
                continue;

            try
            {
                if (File.Exists(to))
                    File.Delete(to);
                File.Move(from, to);
            }
            catch (Exception)
            {
                // Best-effort: a locked or vanished history file shouldn't block opening this run's log.
            }
        }

        return SlotPath(0);
    }

    private void CloseWriter()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }
}
