namespace Toybox.Studio.Toolbar;

/// <summary>
/// The panel edge or corner an overlay toolbar docks against (drag its grip to move it). The edge
/// members centre the toolbar along that edge; the corner members tuck it into the corner. New
/// members append — the value persists numerically inside saved layouts.
/// </summary>
public enum ToolbarEdge
{
    Top,
    Bottom,
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}
