using System.Diagnostics;

namespace Toybox.Studio.Services.Dialogs;

/// <summary>
/// Reveals a path in the OS file explorer — selecting the file when it exists, otherwise opening the
/// nearest existing folder. Best-effort: failures (missing path, no shell) are swallowed.
/// </summary>
public static class FileReveal
{
    public static void InExplorer(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            if (File.Exists(path))
            {
                // explorer /select highlights the file inside its folder.
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else
            {
                var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
        }
        catch
        {
            // Revealing a file is a convenience; never let it surface as an error.
        }
    }
}
