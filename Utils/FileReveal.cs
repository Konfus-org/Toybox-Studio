using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Toybox.Studio.Utils;

/// <summary>
/// Opens the OS file browser with a file selected — Windows Explorer's <c>/select</c>, macOS Finder's
/// <c>-R</c>, or (lacking a universal "reveal" verb on Linux) the containing folder. The path is normalised
/// to the platform separator first: engine-reported paths are forward-slashed, which Explorer's
/// <c>/select</c> silently rejects. Returns a failure <see cref="Result"/> rather than throwing when the
/// shell can't reveal it.
/// </summary>
public static class FileReveal
{
    public static Result Reveal(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return Result.Fail("No path to reveal.");

        var path = absolutePath.Replace('/', Path.DirectorySeparatorChar);
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start(new ProcessStartInfo("open", $"-R \"{path}\"") { UseShellExecute = true });
            else
            {
                var directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory))
                    return Result.Fail("The file has no containing folder to open.");
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
            }

            return Result.Ok();
        }
        catch (Exception exception)
        {
            return Result.Fail(exception.Message);
        }
    }
}
