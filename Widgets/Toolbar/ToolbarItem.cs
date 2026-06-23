namespace Toybox.Studio.Widgets.Toolbar;

/// <summary>
/// One toolbar tool: an icon plus a (multi-part) command, with optional radio-group state so a set of tools
/// can show a single active "checked" member (the transform gizmo tools are the first such group). Plain
/// data so users can eventually author their own; persisted as part of the dock layout.
/// </summary>
public sealed class ToolbarItem
{
    /// <summary>Stable id (e.g. <c>builtin.gizmo.move</c>); used as the reconcile key for the row.</summary>
    public string Id { get; set; } = "";

    /// <summary>Lucide icon name (see <c>IconView</c>).</summary>
    public string Icon { get; set; } = "";

    /// <summary>Optional icon colour token (see <c>IconView</c>); null = themed default.</summary>
    public string? IconColor { get; set; }

    /// <summary>The button's tooltip.</summary>
    public string Tooltip { get; set; } = "";

    /// <summary>The command run when the tool is clicked.</summary>
    public ToolCommand Command { get; set; } = new();

    /// <summary>
    /// When this tool is shown relative to play mode. Default <see cref="GameModeCondition.Any"/> (always
    /// shown, e.g. the viewport transform tools); the game transport uses Off (Play) and On (Stop, Pause).
    /// </summary>
    public GameModeCondition GameMode { get; set; } = GameModeCondition.Any;

    /// <summary>
    /// Radio-group key; null = a plain action button. Members of the same group render as toggle buttons and
    /// show a single active member.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Which group-active state marks this tool checked (compared against <c>ToolbarState.GetActive(Group)</c>);
    /// null for stateless tools.
    /// </summary>
    public string? ActiveStateKey { get; set; }
}
