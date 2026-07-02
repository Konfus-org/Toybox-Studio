using Newtonsoft.Json.Linq;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The mutable list an <see cref="ArrayPropertyViewModel"/> edits, abstracted over its two value sources: a live
/// <see cref="JArray"/> in a describe body (the JSON path — engine-authored data Studio can't model) and a real
/// <see cref="System.Collections.IList"/> in the typed model (the reflection path — e.g. a <c>List&lt;Lod&gt;</c>).
/// Both mutate their container in place for add/remove/reorder/duplicate and expose each visible element paired
/// with its descriptor + a live accessor; the list view-model keys its rows by the opaque <c>Key</c> each element
/// carries (a stable reference across structural edits) and re-commits through the accessor after any change.
/// </summary>
public interface IArrayBacking
{
    /// <summary>Whether entries can be added, reordered, and deleted (a resizable list on an editable grid).</summary>
    bool IsResizable { get; }

    /// <summary>The structural type token of a fresh element, so an empty list can still accept a typed paste.</summary>
    string ElementType { get; }

    /// <summary>Each non-hidden element, paired with a stable key (for row reuse and structural targeting), its
    /// descriptor, and a live accessor. The key survives add/remove/reorder so a structural edit reuses the rows.</summary>
    IReadOnlyList<(object Key, PropertyDescriptor Descriptor, IValueAccessor Accessor)> VisibleElements();

    /// <summary>Appends a fresh default element to the list.</summary>
    void Add();

    /// <summary>Removes the element identified by <paramref name="key"/>.</summary>
    void Remove(object key);

    /// <summary>Inserts a copy of the element identified by <paramref name="key"/> right after it.</summary>
    void Duplicate(object key);

    /// <summary>Moves the element <paramref name="key"/> to just before/after the element <paramref name="target"/>
    /// (<paramref name="after"/> distinguishes a downward move, which lands after the target).</summary>
    void Move(object key, object target, bool after);

    /// <summary>Appends a copy of a bare element value (a pasted item) to the end of the list.</summary>
    void AppendValue(JToken value);

    /// <summary>Replaces the whole list with a default array (the settings reset path); returns false when the
    /// backing can't be reset from the given token (the typed path has no engine-default array).</summary>
    bool ResetTo(JToken token);
}
