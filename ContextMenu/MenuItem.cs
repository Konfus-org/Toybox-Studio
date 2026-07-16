using Avalonia.Media;
using System.Threading.Tasks;
using System;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// One code-defined context-menu row: its display (icon / label / shortcut / search keywords), whether it is
/// enabled, and the action it runs when chosen. A <see cref="ContextMenu{T}"/> assembles these fresh each time a
/// menu opens via the fluent <see cref="ItemBuilder"/>, including only the rows that make sense for what was
/// clicked — so a hidden row is simply omitted; <see cref="IsEnabled"/> is for a row that should show but be
/// greyed out. A separator carries no action. The <see cref="Label"/> doubles as the favorites key.
/// </summary>
public sealed class MenuItem
{
    public string Label { get; init; } = "";

    public Icon Icon { get; init; }

    /// <summary>The icon's colour (e.g. <c>Colors.Red</c>); null inherits the surrounding ink.</summary>
    public Color? IconColor { get; init; }

    /// <summary>Optional shortcut hint shown right-aligned (e.g. <c>Ctrl+C</c>); display only.</summary>
    public string? Gesture { get; init; }

    /// <summary>Extra words this row matches on in the menu's search box, beyond its label.</summary>
    public string? Keywords { get; init; }

    /// <summary>Whether the row is clickable. A disabled row shows greyed out (with <see cref="DisabledReason"/>
    /// as a tooltip) rather than being hidden — use it when the action is contextually unavailable but worth
    /// surfacing.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>Tooltip explaining why a disabled row is unavailable; null when enabled.</summary>
    public string? DisabledReason { get; init; }

    /// <summary>The action run when the row is chosen; null for a separator.</summary>
    public Func<Task>? Run { get; init; }

    public bool IsSeparator { get; init; }

    /// <summary>A divider row (no icon/label/action).</summary>
    public static MenuItem Separator() => new() { IsSeparator = true };
}
