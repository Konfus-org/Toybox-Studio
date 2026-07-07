namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// A list element row's drag handle, in its left slot. Carries what the <see cref="ListReorder"/> drag
/// behavior needs: the element's live index (resolved at access time, so it survives reorders) and the
/// move that commits a step of the drag.
/// </summary>
public sealed class ReorderHandleViewModel(Func<int> index, Action<int, int> move)
{
    /// <summary>The element's current position in its list.</summary>
    public int Index => index();

    public void Move(int from, int to) => move(from, to);
}
