using Avalonia.Controls;
using Toybox.Studio.Shell.Workspace;

namespace Toybox.Studio.EntityInspector;

[Dockable(Title = "Inspector", Icon = Icon.SlidersHorizontal, IconColor = Toybox.Studio.Utils.PaletteColor.Yellow, Slot = DockSlot.Right, Proportion = 0.25,
    Width = 360, Height = 600, Order = 0)]
public partial class EntityInspectorView : UserControl
{
    public EntityInspectorView()
    {
        InitializeComponent();
    }
}
