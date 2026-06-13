using Avalonia.Controls;
using Toybox.Studio.Workspace;

namespace Toybox.Studio.Widgets.Viewport;

[Dockable("Viewport", Title = "Viewport", Slot = DockSlot.CenterTop, Proportion = 0.72,
    Width = 960, Height = 600, Order = 0)]
public partial class ViewportView : UserControl
{
    public ViewportView()
    {
        InitializeComponent();
    }
}
