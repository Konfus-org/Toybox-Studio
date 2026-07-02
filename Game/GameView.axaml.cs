using Avalonia.Controls;
using Toybox.Studio.Shell.Workspace;

namespace Toybox.Studio.Game;

[Dockable(Title = "Game", Icon = Icon.Gamepad2, IconColor = Toybox.Studio.Utils.PaletteColor.Green, Slot = DockSlot.CenterTop, Order = 1,
    Width = 960, Height = 600)]
public partial class GameView : UserControl
{
    public GameView()
    {
        InitializeComponent();
    }
}
