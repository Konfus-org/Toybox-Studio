using Avalonia.Controls;

namespace Toybox.Studio.Toolbar;

/// <summary>
/// The movable toolbar overlay a viewport hosts as a sibling above its input surface. See
/// <see cref="ToolbarViewModel"/>; the grip's drag-to-dock gesture is <see cref="ToolbarDockDrag"/>.
/// </summary>
public partial class ToolbarView : UserControl
{
    public ToolbarView()
    {
        InitializeComponent();
    }
}
