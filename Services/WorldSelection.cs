namespace Toybox.Studio.Services;

/// <summary>
/// Shares the currently selected scene entity between widgets.
/// </summary>
public sealed class WorldSelection
{
    public WorldNode? Selected { get; private set; }

    public event Action<WorldNode?>? SelectionChanged;

    public void Select(WorldNode? node)
    {
        if (ReferenceEquals(Selected, node))
            return;

        Selected = node;
        SelectionChanged?.Invoke(node);
    }
}
