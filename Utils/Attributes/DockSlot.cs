namespace Toybox.Studio.Utils.Attributes;

/// <summary>
/// The edge a dockable sits on in the default layout, relative to its region. With no
/// <see cref="DockableAttribute.Parent"/> the region is the main window: <see cref="Left"/> and
/// <see cref="Right"/> are the side columns, <see cref="Top"/> and <see cref="Bottom"/> the rows of the
/// center column (<see cref="Top"/> is the window's home dock area). With a parent, the dockable's dock
/// splits off the dock containing that parent on this edge. Dockables sharing the same parent and slot
/// tab together. <see cref="Float"/> means the dockable is registered and openable (Window menu, code)
/// but not placed in the default dock — opening it shows a floating window at the attribute's declared
/// float bounds.
/// </summary>
public enum DockSlot
{
    Top,
    Left,
    Right,
    Bottom,
    Float,
}
