using Avalonia.Controls;
using Toybox.Studio.Theming;
using Toybox.Studio.Shell.Workspace;

namespace Toybox.Studio.Settings;

/// <summary>
/// Settings UI, hosted as a floating dockable tool. Authoring a new theme is driven entirely by the
/// view-model's CreateTheme command (via <see cref="ThemeCreator"/>), so the view holds no logic.
/// </summary>
[Dockable(Title = "Settings", Icon = Icon.Settings, IconColor = Toybox.Studio.Utils.PaletteColor.LightGrey, Slot = DockSlot.Float, Width = 680, Height = 620)]
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }
}
