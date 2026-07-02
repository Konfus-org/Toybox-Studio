using System.Collections.Generic;
using System.Linq;
using Toybox.Studio.Favorites;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The fluent surface a <see cref="ContextMenu{T}"/> builds its rows on: add an <see cref="Item"/> (returning an
/// <see cref="ItemBuilder"/> to configure) or a <see cref="Separator"/>, in order. Handed to
/// <see cref="ContextMenu{T}.Build"/> fresh each open, then it composes the authored rows into the shared
/// searchable/favoritable surface — dropping hidden rows and collapsing the menu to nothing when no real row
/// remains.
/// </summary>
public sealed class MenuBuilder
{
    private readonly List<ItemBuilder> _items = [];

    internal MenuBuilder()
    {
    }

    /// <summary>Adds a row with the given label and (optional) Lucide icon; configure it via the returned builder.</summary>
    public ItemBuilder Item(string label, Icon icon = Icon.None)
    {
        var item = new ItemBuilder(label, icon);
        _items.Add(item);
        return item;
    }

    /// <summary>Adds a divider. Leading, trailing and doubled dividers are coalesced away when the menu renders.</summary>
    public void Separator() => _items.Add(ItemBuilder.Separator());

    // Builds the visible rows into the shared menu surface; null when nothing real is left to show. All context
    // menus share one favorites bucket (their item ids are already globally namespaced).
    internal SearchableMenuViewModel? Compose(FavoritesManager favorites)
    {
        var items = _items.Where(item => item.IsVisible).Select(item => item.Build()).ToList();
        if (!items.Any(item => !item.IsSeparator))
            return null;

        var menu = new SearchableMenuViewModel(
            close => items
                .Select(item => new MenuEntryViewModel(item, FavoritesManager.ContextMenuHost, favorites, close))
                .ToList(),
            favorites);
        return menu.IsEmpty ? null : menu;
    }
}
