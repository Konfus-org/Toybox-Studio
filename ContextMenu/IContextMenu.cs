using System;
using System.Threading.Tasks;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// One surface's context menu. Each implementation (a <see cref="ContextMenu{T}"/> subclass) owns a single
/// target type — the view-model the user right-clicked — and knows only its own domain; the
/// <see cref="ContextMenuCatalog"/> routes a clicked object to the matching one by type, so no central type has
/// to import every domain. This non-generic face is what the catalog stores and invokes.
/// </summary>
public interface IContextMenu
{
    /// <summary>The type this menu is shown for — a <see cref="ContextMenu{T}"/>'s type parameter.</summary>
    Type Target { get; }

    /// <summary>Builds the menu for the right-clicked object, or null when there is nothing to show. The object
    /// is guaranteed assignable to the menu's <see cref="Target"/> type by the catalog.</summary>
    Task<SearchableMenuViewModel?> BuildAsync(object target);
}
