using Avalonia.Controls;

namespace Toybox.Studio.Viewport;

/// <summary>
/// A reusable Blender-style split grid of <see cref="ViewportView"/> panes (embedded by a host dockable
/// such as the world editor's <c>WorldViewerView</c>). Dragging from any pane corner splits it into two
/// live viewports (resizing under the pointer); dragging a corner outward merges it back. The split is
/// pure view/behavior — the container and corner behavior live in the Behaviors project — so this view
/// just supplies the pane template and binds the grid.
/// </summary>
public partial class ViewportSplitView : UserControl
{
    public ViewportSplitView()
    {
        InitializeComponent();
    }
}
