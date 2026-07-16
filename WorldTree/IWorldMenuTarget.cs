namespace Toybox.Studio.WorldTree;

/// <summary>
/// Something the world context menu can open over — a world-tree row or the tree's blank area. Routing is
/// by type, so every world surface implements this and the menu is declared once for
/// <c>IWorldMenuTarget</c>. <see cref="EntityId"/> is the right-clicked entity (null over empty space, so
/// the menu shows its add / paste rows instead of the entity verbs), and <see cref="Surface"/> hides the
/// tree-only rows (rename) when a click came from elsewhere. Lives here (not in the context-menu project)
/// so the world-tree view-models can implement it without a project cycle.
/// </summary>
public interface IWorldMenuTarget
{
    /// <summary>The right-clicked entity, or null when empty background space was clicked.</summary>
    ulong? EntityId { get; }

    /// <summary>Which world surface opened the menu — governs the tree-only rows.</summary>
    WorldMenuSurface Surface { get; }
}
