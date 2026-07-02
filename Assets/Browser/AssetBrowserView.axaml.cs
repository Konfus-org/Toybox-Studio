using Avalonia.Controls;
using Toybox.Studio.Shell.Workspace;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// The Asset Browser dockable. Docks beside the Console at the bottom of the default layout; auto-registers
/// (view-model and Windows-menu entry) through the <see cref="DockableAttribute"/>.
/// </summary>
[Dockable(Title = "Assets", Icon = Icon.Box, IconColor = Toybox.Studio.Utils.PaletteColor.Magenta, Slot = DockSlot.CenterBottom, Proportion = 0.34,
    Order = 1, Width = 1000, Height = 380)]
public partial class AssetBrowserView : UserControl
{
    public AssetBrowserView()
    {
        InitializeComponent();
    }
}
