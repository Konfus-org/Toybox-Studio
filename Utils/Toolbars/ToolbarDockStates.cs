namespace Toybox.Studio.Utils.Toolbars;

/// <summary>
/// The keyed bag of toolbar placements a panel persists inside its dock record — one
/// <see cref="ToolbarDockState"/> per hosted toolbar, keyed by the toolbar's stable key. A missing
/// entry is created on first access at the toolbar's own default placement, so a layout saved
/// before a toolbar existed binds it at its default and the new entry rides the next layout save.
/// </summary>
public sealed class ToolbarDockStates
{
    /// <summary>The persisted placements by toolbar key; public so the layout store round-trips it.</summary>
    public Dictionary<string, ToolbarDockState> States { get; set; } = new();

    /// <summary>The placement persisted for <paramref name="key"/>, created at
    /// <paramref name="defaultEdge"/> when the bag has none yet.</summary>
    public ToolbarDockState For(string key, ToolbarEdge defaultEdge)
    {
        if (!States.TryGetValue(key, out var state))
            States[key] = state = new ToolbarDockState { Edge = defaultEdge };
        return state;
    }
}
