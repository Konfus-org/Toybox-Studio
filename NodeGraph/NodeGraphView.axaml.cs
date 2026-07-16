using Avalonia.Controls;

namespace Toybox.Studio.NodeGraph;

/// <summary>
/// The viewport node overlay control. Its only code-behind concern is reporting its pixel size to the
/// view-model: the overlay fills the surface's image rect, so its bounds are the coordinate space the
/// projected (u, v) positions map into.
/// </summary>
public partial class NodeGraphView : UserControl
{
    public NodeGraphView()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
            (DataContext as NodeGraphViewModel)?.SetViewSize(e.NewSize.Width, e.NewSize.Height);
    }
}
