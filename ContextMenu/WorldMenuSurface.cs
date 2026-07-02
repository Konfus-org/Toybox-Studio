namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The world surface a context menu opened over. It governs the tree-only rows (inline rename, sibling reorder)
/// that are view-layer actions the world tree owns and make no sense over the 3D viewport.
/// </summary>
public enum WorldMenuSurface
{
    /// <summary>The world tree panel — its rows and its blank area.</summary>
    Tree,

    /// <summary>The 3D viewport — billboards, raycast hits, and empty-space misses.</summary>
    Viewport,
}
