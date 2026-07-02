using Avalonia.Controls;
using Toybox.Studio.Shell.Workspace;

namespace Toybox.Studio.LogConsole;

[Dockable(Title = "Console", Icon = Icon.Terminal, IconColor = Toybox.Studio.Utils.PaletteColor.Grey, Slot = DockSlot.CenterBottom, Proportion = 0.28,
    Width = 800, Height = 320, Order = 0)]
public partial class LogConsoleView : UserControl
{
    public LogConsoleView()
    {
        InitializeComponent();
    }
}
