using Avalonia.Controls;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.SettingsEditor;

[Dockable(Title = "Settings", Icon = "Settings", Slot = DockSlot.Float, ShowInWindowMenu = false,
    FloatWidth = 620, FloatHeight = 640)]
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }
}
