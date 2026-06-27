using System;
using System.Threading.Tasks;

namespace Toybox.Studio.Widgets.ContextMenu;

/// <summary>
/// One code-defined context-menu row: its display (icon / label / shortcut / search keywords) and the action
/// it runs when chosen. The editor-side builders in <see cref="ContextMenuService"/> assemble these fresh each
/// time a menu opens, including only the rows that make sense for what was clicked — so there is no separate
/// visibility flag here. A separator carries no action. <see cref="Id"/> is the stable favorites key.
/// </summary>
public sealed class MenuItem
{
    public required string Id { get; init; }

    public string Label { get; init; } = "";

    public string Icon { get; init; } = "";

    public string? IconColor { get; init; }

    /// <summary>Optional shortcut hint shown right-aligned (e.g. <c>Ctrl+C</c>); display only.</summary>
    public string? Gesture { get; init; }

    /// <summary>Extra words this row matches on in the menu's search box, beyond its label.</summary>
    public string? Keywords { get; init; }

    /// <summary>The action run when the row is chosen; null for a separator.</summary>
    public Func<Task>? Run { get; init; }

    public bool IsSeparator { get; init; }

    /// <summary>A divider row (no icon/label/action).</summary>
    public static MenuItem Separator() => new() { Id = "", IsSeparator = true };
}
