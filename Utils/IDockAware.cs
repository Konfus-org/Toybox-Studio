namespace Toybox.Studio.Utils;

/// <summary>
/// Lets a dockable's view-model react to its panel opening and closing — e.g. Settings builds its
/// editable draft on open and discards it on close. The window manager calls these once per open
/// panel (not per re-templating pass). Lives in Utils so feature projects can implement it without
/// referencing the workspace.
/// </summary>
public interface IDockAware
{
    /// <summary>Called when the dockable's panel opens (including on layout restore).</summary>
    void OnDockOpened();

    /// <summary>Called when the dockable's panel closes.</summary>
    void OnDockClosed();
}
