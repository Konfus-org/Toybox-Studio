using System;
using System.Linq;
using Avalonia;
using Dock.Avalonia.Controls;

namespace Toybox.Studio.Docking;

/// <summary>
/// A Dock <see cref="HostWindow"/> that nudges itself fully onto a visible screen when it opens. Dock
/// positions a floated (or restored) window at the coordinates its <c>DockWindow</c> model carries —
/// which, for a layout saved on a since-disconnected monitor or a window dragged past a screen edge
/// before exit, can leave the title bar (and its close button) off-screen and unreachable. Clamping once
/// on open, against the working area of the screen the window lands on, keeps the title bar reachable
/// without fighting the user's later drags (which are free to move it partly off-screen again).
/// </summary>
internal sealed class ClampedHostWindow : HostWindow
{
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ClampIntoWorkingArea();
    }

    private void ClampIntoWorkingArea()
    {
        if (Screens is not { } screens)
            return;

        // The screen the window's top-left lands on; when it's off every screen (fully off-desktop), fall
        // back to the primary so the window is pulled back into view.
        var screen = screens.All.FirstOrDefault(candidate => candidate.Bounds.Contains(Position))
            ?? screens.Primary;
        if (screen is null)
            return;

        var area = screen.WorkingArea;

        // The window's pixel size, from its frame (chrome included) when known, else its client size.
        var frame = FrameSize ?? ClientSize;
        var width = (int)Math.Ceiling(frame.Width * DesktopScaling);
        var height = (int)Math.Ceiling(frame.Height * DesktopScaling);

        // Keep the whole window inside the working area when it fits; when it's larger than the area, pin
        // it to the top-left so at least the title bar and its controls stay reachable.
        var maxX = Math.Max(area.X, area.Right - width);
        var maxY = Math.Max(area.Y, area.Bottom - height);
        var x = Math.Clamp(Position.X, area.X, maxX);
        var y = Math.Clamp(Position.Y, area.Y, maxY);

        if (x != Position.X || y != Position.Y)
            Position = new PixelPoint(x, y);
    }
}
