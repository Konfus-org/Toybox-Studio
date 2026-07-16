namespace Toybox.Studio.Toolbar;

/// <summary>
/// A panel toolbar's persisted placement — the instance the panel's dock record serializes into the
/// saved layout and the toolbar view-model writes through on every dock, so the exit-time layout save
/// persists edge changes with no per-edit save (each panel instance keeps its own).
/// </summary>
public sealed class ToolbarDockState
{
    public ToolbarEdge Edge { get; set; } = ToolbarEdge.Top;
}
