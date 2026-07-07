using Avalonia.Controls;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.SettingsEditor;

[Dockable(Title = "Settings", Icon = "Settings", Width = 620, Height = 640)]
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }
}
