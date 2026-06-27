using Avalonia.Controls;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// The reusable surface fragment (zero-copy GPU image + ghosts), bound to a
/// <see cref="ViewportSurfaceViewModel"/>. Embedded by every specific viewport so the interop/ghost markup
/// lives in one place.
/// </summary>
public partial class ViewportSurfaceView : UserControl
{
    public ViewportSurfaceView()
    {
        InitializeComponent();
    }
}
