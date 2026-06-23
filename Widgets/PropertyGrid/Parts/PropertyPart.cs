using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Which side of the row a part sits on. The shared <see cref="PropertyRow"/> chrome renders one
/// <c>ItemsControl</c> per slot, so the set of parts in each is fully dynamic.
/// </summary>
public enum PartSlot
{
    /// <summary>Inside the indented label gutter, before the type icon (so it inherits the per-depth indent
    /// and reads as a tree node): the disclosure chevron, the drag handle.</summary>
    Leading,

    /// <summary>After the value editor, on the right edge: the add/remove affordances, the state indicator.</summary>
    Trailing,
}

/// <summary>
/// Base for a "PropertyPart" — one composable piece of a property-grid row (the drag handle, the add/remove
/// affordances, the disclosure chevron, the state indicator). A <see cref="PropertyViewModel"/> carries a
/// dynamic <see cref="PropertyViewModel.Parts"/> list of these, and the shared <see cref="PropertyRow"/>
/// chrome lays each out in the <see cref="Slot"/> it declares. Each concrete part is its own view-model type
/// so it pairs with its own View via a DataTemplate in PropertyGridView.axaml.
/// </summary>
public abstract class PropertyPart(PartSlot slot, int order = 0) : ObservableObject
{
    /// <summary>The row region this part renders in.</summary>
    public PartSlot Slot { get; } = slot;

    /// <summary>
    /// Sort key within the slot (ascending = closer to the value editor's outer edge first). Parts are added
    /// in whatever order their owners happen to run, so this — not insertion order — fixes the visual layout:
    /// the handle sits left of the chevron, and add/remove sits left of the always-rightmost state indicator.
    /// Parts with equal order keep insertion order.
    /// </summary>
    public int Order { get; } = order;
}
