using Avalonia.Controls;

namespace Toybox.Studio.Coding;

/// <summary>
/// The reusable code editor control: the Monaco surface, tab strip, hot-reload bolt, ASCII-ghost overlay, and
/// status bar. It is a plain <see cref="UserControl"/> (no <c>[Dockable]</c>) so it can be hosted by the
/// dockable <see cref="CoderPanelView"/> or embedded in an inspector strip; its data context is a
/// <see cref="CoderViewModel"/>.
/// </summary>
public partial class CoderView : UserControl
{
    public CoderView()
    {
        InitializeComponent();
    }
}
