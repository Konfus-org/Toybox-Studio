namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// The drag-to-reorder grip part, present only on reorderable list elements. Carries the references the
/// <see cref="ListReorder"/> behavior needs — the owning list and this element — so the grip can live in its
/// own row slot (its DataContext is this part, not the element view-model).
/// </summary>
public sealed class HandlePart : PropertyPart
{
    // Order 0: the grip sits at the far left of the leading slot, before the disclosure chevron.
    public HandlePart(ArrayPropertyViewModel list, PropertyViewModel element) : base(PartSlot.Leading, order: 0)
    {
        List = list;
        Element = element;
    }

    /// <summary>The list this element belongs to (the reorder target).</summary>
    public ArrayPropertyViewModel List { get; }

    /// <summary>The element row this handle reorders.</summary>
    public PropertyViewModel Element { get; }
}
