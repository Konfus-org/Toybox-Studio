using Avalonia.Controls;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.WorldViewer;

/// <summary>
/// The world editor viewport dockable — a Blender-style split grid of engine viewports (the generic
/// <c>ViewportSplitView</c>) wired for world editing: transform + render-layers toolbars, click-select,
/// and the Q/W/E/R transform tools scoped to this panel. It is the panel identity: its view-model type
/// (<see cref="WorldViewerViewModel"/>, by the <c>XxxView → XxxViewModel</c> convention) is both the
/// dockable key and the input scheme scope the world viewport actions bind under.
/// </summary>
[Dockable(Title = "World Viewer", Icon = "Axis3d", Slot = DockSlot.Top, Singleton = false,
    FloatWidth = 960, FloatHeight = 600)]
public partial class WorldViewerView : UserControl
{
    public WorldViewerView()
    {
        InitializeComponent();
    }
}
