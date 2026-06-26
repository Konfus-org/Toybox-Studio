namespace Toybox.Studio.Services.Commands;

/// <summary>
/// What a context menu was opened over — the target a data-driven <c>editor.*</c> command acts on. Entity
/// commands (delete, move, duplicate, clipboard) operate on the current <see cref="World.WorldSelection"/>,
/// which the menu-open gesture sets first, so they need no target here; component commands need to know which
/// component header was right-clicked, and background menus need to know they were opened over empty space.
/// A plain, immutable carrier threaded through <c>ToolCommandRunner.RunAsync</c> to <c>EditorCommands</c>.
/// </summary>
public sealed class MenuContext
{
    /// <summary>The menu definition this context belongs to (e.g. <c>worldTree.entity</c>); the favorites scope.</summary>
    public string Host { get; init; } = "";

    /// <summary>The entity the menu targets (the right-clicked / primary selection); null for a background menu.</summary>
    public ulong? EntityId { get; init; }

    /// <summary>The component the menu targets (an inspector component header); null otherwise.</summary>
    public string? Component { get; init; }

    /// <summary>True when the menu was opened over empty space rather than an item (an "add / paste here" menu).</summary>
    public bool IsBackground { get; init; }
}
