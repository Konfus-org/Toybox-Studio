using System.Collections.Generic;

namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// A named context menu: an id (the host key, e.g. <c>worldTree.entity</c>) and its ordered items. The built-in
/// definitions are produced as data by <see cref="MenuCatalogDefaults"/> and resolved (with any user overrides
/// merged in) through <see cref="MenuCatalog"/>; the id doubles as the favorites scope for the menu's items.
/// </summary>
public sealed class MenuDefinition
{
    /// <summary>The menu's stable id / host key (e.g. <c>inspector.component</c>).</summary>
    public string Id { get; set; } = "";

    /// <summary>The menu's items, in display order.</summary>
    public List<MenuEntry> Items { get; set; } = [];
}
