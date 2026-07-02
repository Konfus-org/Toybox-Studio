using Avalonia.Controls;
using Toybox.Studio.Shell.Workspace;

namespace Toybox.Studio.Viewport;

[Dockable(Title = "Viewport", Icon = Icon.Axis3d, IconColor = Toybox.Studio.Utils.PaletteColor.Blue, Slot = DockSlot.CenterTop, Proportion = 0.72,
    Width = 960, Height = 600, Order = 0, Singleton = false)]
public partial class ViewportView : UserControl
{
    public ViewportView()
    {
        InitializeComponent();
    }
}
