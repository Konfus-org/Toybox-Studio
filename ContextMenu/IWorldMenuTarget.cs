namespace Toybox.Studio.ContextMenu;

/// <summary>
/// Something the one <see cref="WorldContextMenu"/> can open over — a world-tree row, the tree's blank area, a
/// viewport billboard, a viewport raycast hit, or a viewport miss. Routing is by type, so every world surface
/// implements this and the menu is declared once for <c>IWorldMenuTarget</c>. <see cref="EntityId"/> is the
/// right-clicked entity (null over empty space, so the menu shows its add / paste rows instead of the entity
/// verbs), and <see cref="Surface"/> hides the tree-only rows (rename, reorder) when the click came from the
/// viewport.
/// </summary>
public interface IWorldMenuTarget
{
    /// <summary>The right-clicked entity, or null when empty background space was clicked.</summary>
    ulong? EntityId { get; }

    /// <summary>Which world surface opened the menu — governs the tree-only rows.</summary>
    WorldMenuSurface Surface { get; }
}
