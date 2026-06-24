using System.Diagnostics;

namespace Toybox.Studio.Services.Dialogs;

/// <summary>
/// Reveals a path in the OS file explorer — selecting the file when it exists, otherwise opening the
/// nearest existing folder. Best-effort: it never throws, but a failure is reported (logged) rather than
/// silently swallowed so a misbehaving shell invocation can be diagnosed.
/// </summary>
public static class FileReveal
{
    /// <summary>
    /// Reveals <paramref name="path"/> in the OS file explorer. Failures are routed to
    /// <paramref name="onError"/> when supplied, otherwise to <see cref="Trace"/>, instead of being swallowed.
    /// </summary>
    public static void InExplorer(string? path, Action<string>? onError = null)
    {
        if (string.IsNullOrEmpty(path))
            return;

        // The engine reports asset paths in generic (forward-slash) form, but explorer.exe's /select only
        // honours native backslash separators — handed forward slashes it silently opens the default folder
        // instead of revealing the file. Normalise to native separators (and drop any trailing separator,
        // which would otherwise corrupt the /select target) before reaching the shell.
        path = path.Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        try
        {
            if (File.Exists(path))
            {
                // explorer /select highlights the file inside its folder. explorer.exe parses "/select,<path>"
                // as a single combined token, so this is one of the few cases where the legacy Arguments string
                // is required (ArgumentList would quote the whole "/select,..." element and break the switch).
                // Robustness comes from quoting the (already trim-normalised) path so spaces/commas can't split
                // the target; Windows paths can't contain a double quote, so no inner escaping is needed.
                Process.Start(new ProcessStartInfo("explorer.exe")
                {
                    Arguments = $"/select,{Quote(path)}",
                    UseShellExecute = false,
                });
            }
            else
            {
                var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
        }
        catch (Exception exception)
        {
            // Revealing a file is a convenience; never let it surface as an unhandled error — but do report
            // it so a broken invocation is visible instead of a silent no-op.
            var message = $"Could not reveal '{path}' in the file explorer: {exception.Message}";
            if (onError is not null)
                onError(message);
            else
                Trace.WriteLine(message);
        }
    }

    // Wraps a path in double quotes so a space or comma in it can't split the /select target. The target
    // never legitimately contains a double quote (Windows forbids it in paths), so no inner escaping is needed.
    private static string Quote(string path) => $"\"{path}\"";
}
