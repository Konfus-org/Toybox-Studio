using Avalonia.Controls;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.AssetViewer;

/// <summary>
/// The Asset Viewer dockable. Non-singleton: each open owns its own preview world/stream, and reuse is
/// explicit through the <see cref="AssetViewerLauncher"/> (see its Open flow). It opens on demand (from
/// the Asset Browser or File ▸ Open ▸ Asset) docked in the window's center area
/// (<see cref="DockSlot.Center"/>) — a previewable asset opens as a tab beside the World Viewer, not a
/// floating window — while staying out of the default docked layout (which would leave an empty,
/// invisible instance the reuse logic could route opens into). Its tab title tracks the edited asset's
/// name via the owner base.
/// </summary>
[Dockable(Title = "Asset Viewer", Icon = "Image", Slot = DockSlot.Center, Singleton = false)]
public partial class AssetViewerView : UserControl
{
    public AssetViewerView()
    {
        InitializeComponent();
    }
}
