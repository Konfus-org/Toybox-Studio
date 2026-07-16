using Avalonia.Threading;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio;

/// <summary>
/// The app's last lines of defense, so no crash is ever silent. A Release studio logs UI-thread
/// exceptions and survives them (one broken interaction shouldn't take the whole editor and the user's
/// session with it). A Debug studio deliberately does NOT catch them: the exception throws normally,
/// so an attached debugger breaks at the faulting editor code, and the fatal hook still logs it on the
/// way down. Unobserved task exceptions are logged and marked observed, and a genuinely fatal crash —
/// any exception actually terminating the process — is logged AND written synchronously to a crash
/// file beside the logs, because the normal log persists on a background drain the dying process may
/// never run again. Installed once by the composition root as soon as the logger exists.
/// </summary>
public static class CrashGuard
{
    private static Logger? _log;

    /// <summary>Hooks every process-wide exception seam. Call once, as soon as the logger exists.</summary>
    public static void Install(Logger log)
    {
        _log = log;
#if !DEBUG
        // Only a Release studio catches UI-thread exceptions (to survive them). A Debug studio leaves
        // them uncaught on purpose: the throw breaks in the faulting editor code under a debugger, and
        // the crash is still logged by the fatal hook below before the studio goes down.
        Dispatcher.UIThread.UnhandledException += OnDispatcherException;
#endif
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnFatalException;
    }

    /// <summary>
    /// Records a fatal exception caught at the outermost frame (before the hooks exist, or escaping
    /// them). The caller decides whether to rethrow or exit.
    /// </summary>
    public static void ReportFatal(Exception exception) =>
        RecordFatal($"Fatal unhandled exception: {exception}");

#if !DEBUG
    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Survive it: the editor stays up (possibly degraded) and the log carries the full story. An
        // unstable feature is recoverable; a vanished editor with unsaved work is not.
        e.Handled = true;
        _log?.Critical($"Unhandled UI exception (the editor may be unstable): {e.Exception}");
    }
#endif

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Observed-at-finalization failures never crash the process, but they must not vanish either.
        e.SetObserved();
        _log?.Error($"Unobserved task exception: {e.Exception}");
    }

    private static void OnFatalException(object sender, UnhandledExceptionEventArgs e) =>
        RecordFatal($"Fatal unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}");

    private static void RecordFatal(string message)
    {
        _log?.Critical(message);
        WriteCrashFile(message);
    }

    // The crash file is append-only across runs (unlike the rotated log), so repeated crashes build a
    // history and the write is a single synchronous append that needs no infrastructure to still work
    // while the process burns down.
    private static void WriteCrashFile(string message)
    {
        try
        {
            // The container may not exist yet (a crash during startup), so the deterministic, dependency-free
            // paths catalog is constructed directly here rather than injected.
            var paths = new PathsCatalog();
            Directory.CreateDirectory(paths.LogsDirectory);
            File.AppendAllText(
                paths.CrashLogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Nothing left to try — the process is dying and even the crash file couldn't be written.
        }
    }
}
