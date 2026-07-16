namespace Toybox.Studio.Utils.Attributes;

/// <summary>
/// The edge a dockable sits on in the default layout, relative to its region. With no
/// <see cref="DockableAttribute.Parent"/> the region is the main window: <see cref="Left"/> and
/// <see cref="Right"/> are the side columns, <see cref="Top"/> and <see cref="Bottom"/> the rows of the
/// center column (<see cref="Top"/> is the window's home dock area). With a parent, the dockable's dock
/// splits off the dock containing that parent on this edge. Dockables sharing the same parent and slot
/// tab together. <see cref="Center"/> and <see cref="Float"/> both keep the dockable out of the default
/// layout (registered and openable, but not seeded); they differ in where an on-demand open lands —
/// <see cref="Center"/> docks it as a tab in the window's home area (beside the World Viewer), while
/// <see cref="Float"/> opens a floating window at the attribute's declared float bounds. A panel opened
/// by opening a document (the Coder for a script, the Asset Viewer for a previewable asset) uses
/// <see cref="Center"/> so the document lands in the main editing area, not a stray window.
/// </summary>
public enum DockSlot
{
    Top,
    Left,
    Right,
    Bottom,
    Center,
    Float,
}
