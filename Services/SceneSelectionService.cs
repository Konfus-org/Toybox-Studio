namespace Toybox.Studio.Services;

/// <summary>Shares the currently selected scene entity between widgets.</summary>
public sealed class SceneSelectionService
{
    public SceneNode? Selected { get; private set; }

    public event Action<SceneNode?>? SelectionChanged;

    public void Select(SceneNode? node)
    {
        if (ReferenceEquals(Selected, node))
            return;

        Selected = node;
        SelectionChanged?.Invoke(node);
    }
}
