using System.Diagnostics;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Dialogs;

/// <summary>
/// Opens a file path in whatever application the OS associates with it (the shell "open" verb) — the
/// fallback for assets the editor has no in-Studio viewer for. Returns a failure <see cref="Result"/>
/// rather than throwing when the shell can't open it (missing file, no association).
/// </summary>
public static class FileOpen
{
    public static Result OpenWithDefaultApp(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return process is null ? Result.Fail("The OS did not start a handler for the file.") : Result.Ok();
        }
        catch (Exception exception)
        {
            return Result.Fail(exception.Message);
        }
    }
}
