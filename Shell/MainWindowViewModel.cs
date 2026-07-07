using Toybox.Studio.MenuBar;
using Toybox.Studio.Status;
using Toybox.Studio.Viewport;

namespace Toybox.Studio.Shell;

/// <summary>
/// The main window's content: the menu bar on top, the 3D viewport into the engine's world, and the
/// status bar.
/// </summary>
public sealed class MainWindowViewModel
{
    public MainWindowViewModel(MenuBarViewModel menuBar, StatusViewModel status, ViewportViewModel viewport)
    {
        MenuBar = menuBar;
        Status = status;
        Viewport = viewport;
    }

    public string Title => "Toybox Studio";

    public MenuBarViewModel MenuBar { get; }

    public StatusViewModel Status { get; }

    public ViewportViewModel Viewport { get; }
}
