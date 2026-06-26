using System.Collections.Generic;
using Toybox.Studio.Widgets.Toolbar;

namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// One context-menu item: an icon + label plus the (multi-step) command it runs, reusing the same
/// <see cref="ToolCommand"/> the data-driven toolbars run. Plain data so the built-in menus are authored as a
/// list (see <see cref="MenuCatalogDefaults"/>) and users can override/extend them with JSON in
/// <c>~/.toybox/ContextMenus</c>. <see cref="Id"/> is the stable key used for favorites and for user overrides;
/// <see cref="When"/> gates visibility against what the menu was opened over (see <see cref="MenuCondition"/>).
/// </summary>
public sealed class MenuEntry
{
    /// <summary>Stable id (e.g. <c>entity.delete</c>); the favorite key and the user-override merge key.</summary>
    public string Id { get; set; } = "";

    /// <summary>The menu row's label; empty for a separator.</summary>
    public string Label { get; set; } = "";

    /// <summary>Lucide icon name (see <c>IconView</c>); optional.</summary>
    public string Icon { get; set; } = "";

    /// <summary>Optional icon colour token (see <c>IconView</c>); null = themed default.</summary>
    public string? IconColor { get; set; }

    /// <summary>Optional shortcut hint shown right-aligned (e.g. <c>Ctrl+C</c>); display only.</summary>
    public string? InputGesture { get; set; }

    /// <summary>Extra words this item matches on in the menu's search box, beyond its label.</summary>
    public string? Keywords { get; set; }

    /// <summary>The command run when the item is chosen (an <c>editor.*</c> verb or any engine RPC).</summary>
    public ToolCommand Command { get; set; } = new();

    /// <summary>True for a divider row (no icon/label/command); rendered as a separator.</summary>
    public bool IsSeparator { get; set; }

    /// <summary>When this item is shown for what the menu was opened over (default: always).</summary>
    public MenuCondition When { get; set; } = MenuCondition.Always;

    /// <summary>Nested items; when present the row opens a submenu instead of running a command.</summary>
    public List<MenuEntry>? SubItems { get; set; }
}
