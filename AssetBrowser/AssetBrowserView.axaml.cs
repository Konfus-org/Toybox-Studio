using Avalonia.Controls;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// The Asset Browser dockable: a searchable, category-filtered grid of the project's assets. A singleton
/// panel — one browser for the workspace — docked along the bottom edge. Double-tapping a tile opens its
/// asset (previewable ones into the Asset Viewer editor).
/// </summary>
[Dockable(Title = "Assets", Icon = "FolderTree", Slot = DockSlot.Bottom)]
public partial class AssetBrowserView : UserControl
{
    public AssetBrowserView()
    {
        InitializeComponent();
    }
}
