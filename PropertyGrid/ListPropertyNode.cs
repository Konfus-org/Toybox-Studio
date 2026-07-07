using System.Collections;
using IconPacks.Avalonia.Lucide;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The node for a resizable list property: its children mirror the backing <see cref="IList"/> one row
/// per element, and the mutations its slots trigger — add (<see cref="Slots.AddItemViewModel"/>, in the
/// header's right slot), delete (<see cref="Slots.DeleteItemViewModel"/>), drag-reorder
/// (<see cref="Slots.ReorderHandleViewModel"/>) — edit the backing list and the child rows together.
/// Element rows come from the factory-supplied delegate; their accessors resolve the element's index at
/// access time (through <see cref="IndexOf"/>), so a reorder never leaves a row pointing at a stale
/// slot. Element labels are the element's index, renumbered on every mutation.
/// </summary>
public sealed class ListPropertyNode : PropertyNode
{
    private readonly IList _items;
    private readonly Func<ListPropertyNode, PropertyNode> _createElementNode;
    private readonly Func<object?> _createElement;

    /// <param name="createElementNode">Builds the row for the element at the tail of the children (the
    /// row's own accessors should resolve their index through <see cref="IndexOf"/>).</param>
    /// <param name="createElement">Builds the fresh element an add appends.</param>
    public ListPropertyNode(
        string label,
        IList items,
        Func<ListPropertyNode, PropertyNode> createElementNode,
        Func<object?> createElement,
        PackIconLucideKind icon = PackIconLucideKind.None)
        : base(label, children: null, icon)
    {
        // A list renders as a header band even while empty — its add affordance lives there.
        IsHeader = true;
        _items = items;
        _createElementNode = createElementNode;
        _createElement = createElement;
        for (var i = 0; i < items.Count; i++)
            AddChild(createElementNode(this));
        RefreshChrome();
    }

    /// <summary>The element row's current position — element accessors and slots resolve their index
    /// through this at access time, so they stay correct across reorders.</summary>
    public int IndexOf(PropertyNode element) => IndexOfChild(element);

    /// <summary>Appends a fresh element and its row.</summary>
    public void AddNew()
    {
        _items.Add(_createElement());
        AddChild(_createElementNode(this));
        RefreshChrome();
        IsExpanded = true;
        NotifyEdited();
    }

    public void Remove(PropertyNode element)
    {
        var index = IndexOfChild(element);
        if (index < 0)
            return;

        _items.RemoveAt(index);
        RemoveChildAt(index);
        RefreshChrome();
        NotifyEdited();
    }

    public void Move(int from, int to)
    {
        to = Math.Clamp(to, 0, _items.Count - 1);
        if (from < 0 || from >= _items.Count || from == to)
            return;

        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);
        MoveChild(from, to);
        RefreshChrome();
        NotifyEdited();
    }

    /// <summary>Renumbers the element labels and the header's item-count detail after any mutation.</summary>
    private void RefreshChrome()
    {
        for (var i = 0; i < Children.Count; i++)
            Children[i].Label = $"[{i}]";

        Detail = Children.Count == 1 ? "1 item" : $"{Children.Count} items";
    }
}
