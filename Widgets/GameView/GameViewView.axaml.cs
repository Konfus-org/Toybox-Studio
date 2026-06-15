using Avalonia.Controls;
using Toybox.Studio.Workspace;

namespace Toybox.Studio.Widgets.GameView;

[Dockable("GameView", Title = "Game", Slot = DockSlot.CenterTop, Order = 1,
    Width = 960, Height = 600)]
public partial class GameViewView : UserControl
{
    public GameViewView()
    {
        InitializeComponent();
    }
}
