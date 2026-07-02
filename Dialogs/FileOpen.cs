using System;
using System.Diagnostics;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// Opens a file with the operating system's default program for its type (shell-execute). Best-effort: a
/// failure is swallowed rather than thrown, mirroring <see cref="FileReveal"/>. The companion to
/// <see cref="FileReveal"/>, which selects a file in the OS file manager rather than opening it.
/// </summary>
public static class FileOpen
{
    public static void WithDefaultApp(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            return;

        try
        {
            // UseShellExecute lets the OS pick the registered handler for the file's type.
            Process.Start(new ProcessStartInfo(absolutePath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Best-effort; there's nothing actionable if the shell can't open the file.
        }
    }
}
