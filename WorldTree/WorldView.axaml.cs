using Avalonia.Controls;
using Toybox.Studio.Ecs;
using Toybox.Studio.Shell.Workspace;

namespace Toybox.Studio.WorldTree;

[Dockable(Title = "World", Icon = Icon.ListTree, IconColor = Toybox.Studio.Utils.PaletteColor.Cyan, Slot = DockSlot.Left, Proportion = 0.18,
    Width = 320, Height = 600, Order = 0)]
public partial class WorldView : UserControl
{
    public WorldView()
    {
        InitializeComponent();
    }
}
