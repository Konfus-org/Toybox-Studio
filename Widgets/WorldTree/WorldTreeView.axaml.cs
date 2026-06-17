using Avalonia.Controls;
using Toybox.Studio.Widgets.Ecs;
using Toybox.Studio.Shell.Workspace;

namespace Toybox.Studio.Widgets.WorldTree;

[Dockable("World", Title = "World", Slot = DockSlot.Left, Proportion = 0.18,
    Width = 320, Height = 600, Order = 0, ViewModel = typeof(WorldViewModel))]
public partial class WorldTreeView : UserControl
{
    public WorldTreeView()
    {
        InitializeComponent();
    }
}
