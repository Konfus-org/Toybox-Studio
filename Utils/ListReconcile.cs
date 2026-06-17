using System.Collections.ObjectModel;

namespace Toybox.Studio.Utils;

/// <summary>
/// Reconciles an <see cref="ObservableCollection{T}"/> to match a desired sequence in place — removing,
/// inserting, and moving only the items that actually changed — instead of Clear() + re-Add. A Clear raises a
/// Reset that makes a bound <c>ItemsControl</c> drop and rebuild every container, which for a <c>TreeView</c>
/// collapses expansion, resets scroll, and flashes the whole list. Reconciling in place keeps the unchanged
/// containers, so adding or moving one entity touches only that row and feels smooth.
///
/// Items are matched by the element type's own equality. The callers here hold persistent view-models (no
/// <c>Equals</c> override), so matching is by reference — the same instance keeps its container.
/// </summary>
public static class ListReconcile
{
    public static void Apply<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
    {
        // Drop anything no longer wanted (walk backwards so indices stay valid through the removals).
        var keep = new HashSet<T>(desired);
        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!keep.Contains(target[index]))
                target.RemoveAt(index);
        }

        // Walk the desired order, inserting newcomers and sliding existing items into position. Each step
        // leaves target[0..index] equal to desired[0..index], so a single addition is a single Insert.
        for (var index = 0; index < desired.Count; index++)
        {
            var item = desired[index];
            var current = target.IndexOf(item);
            if (current < 0)
                target.Insert(index, item);
            else if (current != index)
                target.Move(current, index);
        }
    }
}
