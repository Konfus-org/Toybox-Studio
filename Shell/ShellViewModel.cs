using Toybox.Studio.Status;
using Toybox.Studio.Viewport;

namespace Toybox.Studio.Shell;

/// <summary>
/// The main window's content: the 3D viewport into the engine's world plus the status bar.
/// </summary>
public sealed class ShellViewModel
{
    public ShellViewModel(StatusViewModel status, ViewportViewModel viewport)
    {
        Status = status;
        Viewport = viewport;
    }

    public string Title => "Toybox Studio";

    public StatusViewModel Status { get; }

    public ViewportViewModel Viewport { get; }
}
