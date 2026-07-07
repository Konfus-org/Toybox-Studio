using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IconPacks.Avalonia.Lucide;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// One row of the property grid: a label plus the four slots that give the row its behavior — the
/// factory composing the node fills each with a slot view-model or leaves it empty. <see cref="Left"/>
/// is the gutter before the label (a list element's reorder handle), <see cref="Value"/> the editor in
/// the stretching middle, <see cref="Right"/> the row action at the trailing edge (a list's add, an
/// element's delete), and <see cref="Indicator"/> the state dot/lock past it. A node optionally carries
/// child nodes (a composite object's members, a list's elements) rendered indented beneath it. Any
/// edit — announced by a slot through <see cref="NotifyEdited"/>, or bubbling up from a descendant —
/// surfaces through <see cref="Edited"/>, so a grid host observes one event per root. Derived nodes
/// (the list node) mutate children through the protected seams, which keep the bubbling
/// subscriptions right.
/// </summary>
public partial class PropertyNode : ObservableObject
{
    private readonly ObservableCollection<PropertyNode> _children = [];

    /// <param name="children">Non-null makes the node a header band (see <see cref="IsHeader"/>), even
    /// when currently childless; null makes a label/value row.</param>
    public PropertyNode(
        string label,
        IEnumerable<PropertyNode>? children = null,
        PackIconLucideKind icon = PackIconLucideKind.None)
    {
        Label = label;
        Icon = icon;
        IsHeader = children is not null;
        _children.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChildren));
        foreach (var child in children ?? [])
            AddChild(child);
    }

    public event Action? Edited;

    [ObservableProperty]
    public partial string Label { get; internal set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>Small muted text at the row's trailing side (a list's "n items"); null for none.</summary>
    [ObservableProperty]
    public partial string? Detail { get; internal set; }

    /// <summary>Renders as a collapsible full-width separator band (a composite's or list's header)
    /// rather than a label/value row.</summary>
    public bool IsHeader { get; internal set; }

    /// <summary>The header's icon; <see cref="HasIcon"/> false means none.</summary>
    public PackIconLucideKind Icon { get; }

    public bool HasIcon => Icon != PackIconLucideKind.None;

    /// <summary>The left-gutter slot, before the label (a list element's reorder handle).</summary>
    public object? Left { get; init; }

    /// <summary>The value slot in the stretching middle, after the label — the editor's home.</summary>
    public object? Value { get; init; }

    /// <summary>The row-action slot at the trailing edge (a list's add, an element's delete).</summary>
    public object? Right { get; init; }

    /// <summary>The state slot past the action (the modified/default dot, the read-only lock).</summary>
    public object? Indicator { get; init; }

    /// <summary>Child rows; also raises collection-change notification when it can grow (lists).</summary>
    public IReadOnlyList<PropertyNode> Children => _children;

    public bool HasChildren => _children.Count > 0;

    /// <summary>Announces an edit on this node — the factory wires each row accessor's
    /// <see cref="PropertyValueAccessor.Changed"/> here.</summary>
    public void NotifyEdited() => Edited?.Invoke();

    protected void AddChild(PropertyNode child)
    {
        child.Edited += NotifyEdited;
        _children.Add(child);
    }

    protected void RemoveChildAt(int index)
    {
        _children[index].Edited -= NotifyEdited;
        _children.RemoveAt(index);
    }

    protected void MoveChild(int from, int to) => _children.Move(from, to);

    protected int IndexOfChild(PropertyNode child) => _children.IndexOf(child);
}
