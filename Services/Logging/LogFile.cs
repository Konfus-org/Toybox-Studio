using System.Collections.Concurrent;
using Toybox.Studio.Services.Settings;
using Toybox.Studio.Services.Project;
namespace Toybox.Studio.Services.Logging;

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
        Path.Combine(EditorSettings.BaseDirectory, "Logs");

    private const string BaseName = "TbxStudio";
    private const string Extension = ".log";
    private const int MaxHistory = 10;

    // How often the background consumer flushes buffered lines to disk. Short enough that a crash loses at
    // most this much tail, long enough that a log flood doesn't thrash the disk.
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);

    private readonly object _sync = new();
    private readonly BlockingCollection<string> _queue = new();
    private readonly Thread _consumer;
    private StreamWriter? _writer;

    public LogFile()
    {
        OpenFreshLog();
        _consumer = new Thread(ConsumeLoop)
        {
            IsBackground = true,
            Name = "TbxStudio.LogFile",
        };
        _consumer.Start();
    }

    /// <summary>
    /// The file currently being written (this run's TbxStudio.log).
    /// </summary>
    public string? CurrentFilePath { get; private set; }

    /// <summary>
    /// Queues one entry for the current log file. The timestamp is captured here (on the producer/calling
    /// thread) so the file reflects when the line was logged and stays correctly ordered, but the actual
    /// disk write happens on a background consumer so the caller — often the UI thread — never blocks on disk.
    /// </summary>
    public void Write(LogEntry entry)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var line = $"[{timestamp}] [{entry.Level.ToWire()}] {entry.Message}";
        // CompleteAdding (in Dispose) can race a concurrent producer; after shutdown the line is simply dropped.
        try
        {
            _queue.Add(line);
        }
        catch (InvalidOperationException)
        {
            // The queue was marked complete during shutdown; nothing more will be written.
        }
    }

    public void Dispose()
    {
        // Stop accepting new lines, let the consumer drain what's queued and flush, then close the file.
        _queue.CompleteAdding();
        if (_consumer.IsAlive)
            _consumer.Join(TimeSpan.FromSeconds(5));

        lock (_sync)
        {
            CloseWriter();
            CurrentFilePath = null;
        }

        _queue.Dispose();
    }

    // Single-consumer drain: writes queued lines in order and flushes on a short interval (and once more
    // when the queue completes), so a crash loses at most one flush interval of tail and shutdown loses none.
    private void ConsumeLoop()
    {
        var lastFlush = DateTime.UtcNow;
        try
        {
            foreach (var line in _queue.GetConsumingEnumerable())
            {
                lock (_sync)
                {
                    _writer?.WriteLine(line);
                }

                if (DateTime.UtcNow - lastFlush >= FlushInterval)
                {
                    FlushWriter();
                    lastFlush = DateTime.UtcNow;
                }
            }
        }
        finally
        {
            // Drain complete (or the loop faulted): flush whatever is buffered so nothing is lost on shutdown.
            FlushWriter();
        }
    }

    private void FlushWriter()
    {
        lock (_sync)
        {
            _writer?.Flush();
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
            // AutoFlush is off: the background consumer flushes on a short interval and on shutdown, so the
            // producer never pays a synchronous per-line disk flush.
            _writer = new StreamWriter(path, append: false) { AutoFlush = false };
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
