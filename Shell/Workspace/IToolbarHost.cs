using Toybox.Studio.Widgets.Toolbar;

namespace Toybox.Studio.Shell.Workspace;

/// <summary>
/// A dockable view-model that hosts a movable toolbar whose state (docked edge + ordered tools) persists in
/// the dock layout. The window manager creates a <see cref="ToolbarTool"/> for such dockables and binds the
/// tool's persisted <see cref="ToolbarLayout"/> into the view-model when the tool materializes.
/// </summary>
public interface IToolbarHost
{
    /// <summary>
    /// Binds the persisted toolbar layout the hosting dock tool owns. Called once per materialized tool;
    /// idempotent across re-templating passes (re-binding the same layout instance is a no-op).
    /// </summary>
    void BindToolbar(ToolbarLayout layout);
}
