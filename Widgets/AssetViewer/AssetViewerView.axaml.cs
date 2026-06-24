using Avalonia.Controls;
using Toybox.Studio.Shell.Workspace;

namespace Toybox.Studio.Widgets.AssetViewer;

// Non-singleton like the viewport: each opened asset spawns its own instance with its own engine
// asset-preview view (and isolated preview world), so several assets can be inspected side by side.
[Dockable("AssetViewer", Title = "Asset Viewer", Slot = DockSlot.Float, Singleton = false,
    Width = 900, Height = 600)]
public partial class AssetViewerView : UserControl
{
    public AssetViewerView()
    {
        InitializeComponent();
    }
}
