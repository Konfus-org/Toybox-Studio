namespace Toybox.Studio.Toolbar;

/// <summary>
/// A dockable view-model hosting movable overlay toolbars. The workspace persists the toolbars'
/// placements with the panel inside the dock layout and hands the persisted bag here when the
/// panel is created — and again on every attach pass of a layout restore, so implementations must be
/// idempotent. Lives in Utils so feature projects can implement it without referencing the workspace.
/// </summary>
public interface IToolbarHost
{
    /// <summary>Binds each hosted toolbar to its persisted placement in <paramref name="states"/>
    /// (re-binding the same instances must no-op).</summary>
    void BindToolbars(ToolbarDockStates states);
}
