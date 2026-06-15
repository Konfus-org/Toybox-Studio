using Avalonia.Controls;
using Toybox.Studio.Theming;
using Toybox.Studio.Workspace;

namespace Toybox.Studio.Shell;

/// <summary>
/// Settings UI, hosted as a floating dockable tool. Authoring a new theme is driven entirely by the
/// view-model's CreateTheme command (via <see cref="ThemeCreator"/>), so the view holds no logic.
/// </summary>
[Dockable("Settings", Title = "Settings", Slot = DockSlot.Float, Width = 680, Height = 620)]
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }
}
