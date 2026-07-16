using Avalonia.Controls;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.GameViewer;

/// <summary>
/// The Game Viewer dockable — a single game viewport with a play / pause / stop / next-frame transport.
/// Non-singleton (like the world viewport): each open owns its own engine game view + stream, disposed
/// when the panel closes. Its view-model type (<see cref="GameViewerViewModel"/>, by the
/// <c>XxxView → XxxViewModel</c> convention) is the panel identity.
/// </summary>
/// <remarks>
/// Docks into the center Top area with no parent, so the default layout tabs it beside the World Viewer
/// (also Slot=Top). <see cref="DockableAttribute.Order"/> 1 sorts it after the World Viewer (Order 0),
/// which makes the World Viewer the first — and therefore the active — tab in that shared dock.
/// </remarks>
[Dockable(Title = "Game", Icon = "Gamepad2", Slot = DockSlot.Top, Order = 1, Singleton = false,
    FloatWidth = 960, FloatHeight = 600)]
public partial class GameViewerView : UserControl
{
    public GameViewerView()
    {
        InitializeComponent();
    }
}
