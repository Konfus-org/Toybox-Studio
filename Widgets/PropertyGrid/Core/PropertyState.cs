namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// The editable state of a property row, shown by the right-hand indicator. Add new states here and a
/// matching case in <see cref="PropertyStateToIndicatorConverter"/> to extend the indicator vocabulary.
/// </summary>
public enum PropertyState
{
    /// <summary>No indicator — composite (object/array) header rows are containers, not values.</summary>
    None,

    /// <summary>Editable value at its default (or whose default is unknown): a hollow circle.</summary>
    Default,

    /// <summary>Editable value that differs from its default: a filled circle.</summary>
    NonDefault,

    /// <summary>Read-only value: a lock.</summary>
    ReadOnly,
}
