using System.Collections;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The typed backing for an <see cref="ArrayPropertyViewModel"/> — a real <see cref="IList"/> from the reflected
/// model (e.g. a <c>List&lt;Lod&gt;</c> or a <c>List&lt;AssetHandle&gt;</c>), mutated in place. Element rows are
/// built from the accessor's per-index cursors (<see cref="IValueAccessor.Element"/>) paired with the element
/// descriptor (a per-element child descriptor when the walk produced one, else the element template's shape). A
/// reference-typed element is keyed by its own object identity (stable across reorder); a value-typed element by a
/// per-index key, so a reorder still reuses rows positionally. Every structural edit re-commits the owning
/// top-level property through the accessor (which reconstructs a fresh object graph, so the change-gated setter
/// pushes).
/// </summary>
internal sealed class TypedArrayBacking : IArrayBacking
{
    private readonly PropertyDescriptor _descriptor;
    private readonly IValueAccessor _accessor;
    private readonly IList? _list;
    private readonly Type _elementType;
    private readonly bool _resizable;

    // Stable per-index keys for a value-typed element list (a boxed value can't be reference-keyed), reused across
    // rebuilds so a positional row survives an unrelated edit. A reference-typed list keys by the element itself.
    private readonly List<object> _indexKeys = [];

    public TypedArrayBacking(PropertyDescriptor descriptor, IValueAccessor accessor)
    {
        _descriptor = descriptor;
        _accessor = accessor;
        _list = accessor.Get() as IList;
        _elementType = EngineSyncValue.ElementTypeOf(accessor.ValueType) ?? typeof(object);
        // A list field is resizable when the model list is a concrete, writable IList on an editable grid.
        _resizable = _list is { IsFixedSize: false, IsReadOnly: false } && accessor.CanWrite;
    }

    public bool IsResizable => _resizable;

    public string ElementType => _descriptor.ElementTemplate?.Type ?? EngineTypes.Unknown;

    public IReadOnlyList<(object Key, PropertyDescriptor Descriptor, IValueAccessor Accessor)> VisibleElements()
    {
        var elements = new List<(object, PropertyDescriptor, IValueAccessor)>();
        if (_list is null)
            return elements;

        // Every element shares the uniform element shape (the template), so a reorder/append can't leave a row
        // bound to a stale per-index descriptor; the per-index ACCESSOR still reads the live (reordered) value.
        var shape = _descriptor.ElementTemplate ?? Leaf();
        for (var index = 0; index < _list.Count; index++)
            elements.Add((KeyFor(index), shape, _accessor.Element(index)));

        return elements;
    }

    public void Add()
    {
        if (_list is not null && _accessor.CreateElement() is { } element)
            _list.Add(element);
    }

    public void Remove(object key)
    {
        if (_list is not null && IndexOf(key) is { } index)
            _list.RemoveAt(index);
    }

    public void Duplicate(object key)
    {
        if (_list is not null && IndexOf(key) is { } index)
            _list.Insert(index + 1, Clone(_list[index]));
    }

    public void Move(object key, object target, bool after)
    {
        if (_list is null || IndexOf(key) is not { } from || IndexOf(target) is not { } to)
            return;

        var element = _list[from];
        _list.RemoveAt(from);
        // The target index shifts by one once the moved element ahead of it is pulled out.
        var insert = _list.IndexOf(target) + (after ? 1 : 0);
        _list.Insert(Math.Clamp(insert, 0, _list.Count), element);
    }

    public void AppendValue(JToken value)
    {
        if (_list is not null)
            _list.Add(EngineSyncValue.ReadBare(_elementType, value));
    }

    // The typed model has no engine-supplied default array to reset to; the settings reset path is JSON-only.
    public bool ResetTo(JToken token) => false;

    // A reference-typed element keys by its own identity (stable across reorder); a value-typed element by a
    // per-index key (reused so positional rows survive). The list is short, so a linear key table is fine.
    private object KeyFor(int index)
    {
        if (!_elementType.IsValueType && _list?[index] is { } element)
            return element;

        while (_indexKeys.Count <= index)
            _indexKeys.Add(new object());
        return _indexKeys[index];
    }

    private int? IndexOf(object key)
    {
        if (_list is null)
            return null;

        if (!_elementType.IsValueType)
        {
            var found = _list.IndexOf(key);
            return found >= 0 ? found : null;
        }

        var slot = _indexKeys.IndexOf(key);
        return slot >= 0 && slot < _list.Count ? slot : null;
    }

    // A deep copy of a typed element through the bare codec, so a duplicate is an independent object graph rather
    // than a shared reference.
    private object? Clone(object? element) =>
        element is null ? null : EngineSyncValue.ReadBare(_elementType, EngineSyncValue.WriteBare(element));

    // The element descriptor for a value list whose walk produced no per-element children (an empty list rebuilt
    // after an append): the element template's shape, or a bare unknown leaf as a last resort.
    private PropertyDescriptor Leaf() => new() { Name = "element", Type = EngineTypes.Unknown };
}
