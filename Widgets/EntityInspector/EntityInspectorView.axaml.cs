using Avalonia.Controls;
using Toybox.Studio.Widgets.Ecs;
using Toybox.Studio.Shell.Workspace;

namespace Toybox.Studio.Widgets.EntityInspector;

[Dockable("Inspector", Title = "Inspector", Slot = DockSlot.Right, Proportion = 0.25,
    Width = 360, Height = 600, Order = 0, ViewModel = typeof(WorldViewModel))]
public partial class EntityInspectorView : UserControl
{
    public EntityInspectorView()
    {
        InitializeComponent();
    }
}
