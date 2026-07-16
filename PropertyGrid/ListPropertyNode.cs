using IconPacks.Avalonia.Lucide;
using System.Collections;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The node for a resizable list property: its children mirror the backing <see cref="IList"/> one row
/// per element, and the mutations its slots trigger — add (<see cref="Slots.AddItemViewModel"/>, in the
/// header's right slot), delete (<see cref="Slots.DeleteItemViewModel"/>), drag-reorder
/// (<see cref="Slots.ReorderHandleViewModel"/>) — edit the backing list and the child rows together.
/// Element rows come from the factory-supplied delegate; their accessors resolve the element's index at
/// access time (through <see cref="IndexOf"/>), so a reorder never leaves a row pointing at a stale
/// slot. An element carrying a non-empty string <c>Name</c> property labels its row (how a scheme, an
/// action, or a keybinding reads as itself); anything else is labeled by its index. Labels refresh on
/// every mutation and edit.
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
            AddElementRow();
        RefreshChrome();

        // A descendant edit may have renamed an element; keep the row labels honest.
        Edited += RefreshChrome;
    }

    /// <summary>The element row's current position — element accessors and slots resolve their index
    /// through this at access time, so they stay correct across reorders.</summary>
    public int IndexOf(PropertyNode element) => IndexOfChild(element);

    /// <summary>Appends a fresh element and its row.</summary>
    public void AddNew()
    {
        _items.Add(_createElement());
        AddElementRow();
        RefreshChrome();
        IsExpanded = true;
        NotifyEdited();
    }

    // Builds an element row and tags it with its owning list, so the context menu on that row can reorder or
    // remove it through this node.
    private void AddElementRow()
    {
        var node = _createElementNode(this);
        node.OwningList = this;
        AddChild(node);
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

    /// <summary>Relabels the element rows (name or index) and the header's item-count detail after
    /// any mutation or edit.</summary>
    private void RefreshChrome()
    {
        for (var i = 0; i < Children.Count; i++)
        {
            var name = NameOf(_items[i]);
            Children[i].Label = string.IsNullOrEmpty(name) ? $"[{i}]" : name;
        }

        Detail = Children.Count == 1 ? "1 item" : $"{Children.Count} items";
    }

    private static string? NameOf(object? item) =>
        item?.GetType().GetProperty("Name")?.GetValue(item) as string;
}
