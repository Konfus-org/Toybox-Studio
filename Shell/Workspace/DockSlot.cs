namespace Toybox.Studio.Shell.Workspace;

/// <summary>
/// Where a dockable sits in the default layout. <see cref="Float"/> means the dockable is registered
/// and openable (Windows menu, Play button, etc.) but is not placed in the default dock — it only
/// appears when the user opens it, as its own floating window.
/// </summary>
public enum DockSlot
{
    Left,
    Right,
    CenterTop,
    CenterBottom,
    Float,
}
