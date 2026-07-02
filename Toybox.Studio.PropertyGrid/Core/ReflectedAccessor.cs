using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils.Extensions;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// A <see cref="IValueAccessor"/> that binds a typed C# member — the reflection-native value source for data
/// Studio models (components, assets and their nested value graphs). It fronts an <see cref="EngineSyncedObject"/>'s
/// generated setter without ever touching JSON in a widget, in two roles.
///
/// <para><b>Top-level</b> — a single <c>[EngineSync]</c> field on the owner, identified by its <c>wire</c>. Reads the
/// LIVE model value through the field's generated property; a <see cref="Set"/> merely STASHES the pending value
/// (it must not write the model, or the change-gated generated setter would see no change at
/// <see cref="Commit"/> time and skip the push). <see cref="Commit"/> serialises the pending value with
/// <see cref="EngineSyncValue.WriteBare"/> and hands it to <see cref="EngineSyncedObject.CommitWire"/>, which
/// routes it through the public setter — pushing to the engine per the field's <see cref="EngineSyncMode"/>
/// (bound component) or dirtying a buffered asset (unbound). Nested accessors reached via <see cref="Member"/>/
/// <see cref="Element"/> mutate this field's live object graph in place and delegate their commit up to this root,
/// so the whole property re-serialises and pushes as one.</para>
///
/// <para><b>Nested</b> — a public property of a nested value object, or an element of a list, reached from a
/// parent accessor. Reads/writes the live object graph directly (nested value objects are reference types, so a
/// <see cref="Set"/> mutates the root field's actual graph; a value-type parent chain is written back to its
/// nearest reference owner). <see cref="Commit"/> delegates to the ROOT top-level accessor.</para>
///
/// <para>An enum member is presented to the dropdown editor as its snake_case choice STRING (the form
/// <see cref="EngineSyncValue.WriteBare"/> writes), so <see cref="EnumPropertyViewModel"/> works unchanged.</para>
/// </summary>
public sealed class ReflectedAccessor : IValueAccessor
{
    // The root top-level accessor a nested accessor commits through; null on the root itself.
    private readonly ReflectedAccessor? _root;

    // The declared CLR type of the value this cursor addresses.
    private readonly Type _valueType;

    // Whether the underlying member can be written (a get-only property is read-only). Read-only [EngineSync]
    // fields arrive wrapped in a ReadOnlyAccessor from the walk, so a top-level accessor treats itself as writable.
    private readonly bool _canWrite;

    // --- Top-level role ---
    private readonly EngineSyncedObject? _owner;
    private readonly string? _wire;
    private readonly Func<object?>? _readModel;
    private object? _pending;
    private bool _hasPending;

    // --- Nested (object member) role ---
    private readonly object? _target;
    private readonly PropertyInfo? _property;

    // The parent accessor whose value contains this member, so a value-type parent can be re-boxed and written
    // back up the chain to its nearest reference owner. Null for a reference-typed (or root-hosted) parent.
    private readonly ReflectedAccessor? _valueTypeParent;

    // --- Nested (list element) role ---
    private readonly IList? _list;
    private readonly int _index;

    // Builds a TOP-LEVEL accessor over one [EngineSync] field of an owner, identified by its wire.
    private ReflectedAccessor(EngineSyncedObject owner, string wire, Type valueType, Func<object?> readModel)
    {
        _owner = owner;
        _wire = wire;
        _valueType = valueType;
        _readModel = readModel;
        _canWrite = true;
    }

    // Builds a NESTED accessor over an object member (property) of a live parent object.
    private ReflectedAccessor(
        ReflectedAccessor root, object? target, PropertyInfo property, ReflectedAccessor? valueTypeParent)
    {
        _root = root;
        _target = target;
        _property = property;
        _valueType = property.PropertyType;
        _canWrite = property.CanWrite;
        _valueTypeParent = valueTypeParent;
    }

    // Builds a NESTED accessor over a list element (by index) of a live parent list.
    private ReflectedAccessor(ReflectedAccessor root, IList list, int index, Type elementType)
    {
        _root = root;
        _list = list;
        _index = index;
        _valueType = elementType;
        _canWrite = !list.IsReadOnly;
    }

    public Type ValueType => _valueType;

    public bool CanWrite => _canWrite;

    /// <summary>Builds the top-level accessor for one <c>[EngineSync]</c> field, reading the live model value
    /// through the owner's generated property. The walk wraps it in a <see cref="ReadOnlyAccessor"/> for a
    /// read-only field.</summary>
    public static ReflectedAccessor ForField(EngineSyncedObject owner, string wire, PropertyInfo property) =>
        new(owner, wire, property.PropertyType, () => property.GetValue(owner));

    public object? Get()
    {
        if (_owner is not null)
        {
            var live = _hasPending ? _pending : _readModel!();
            return AsClrValue(live);
        }

        if (_property is not null)
            return AsClrValue(_property.GetValue(_target));

        if (_list is not null)
            return AsClrValue(_index >= 0 && _index < _list.Count ? _list[_index] : null);

        return null;
    }

    public void Set(object? value)
    {
        var model = FromClrValue(value);

        if (_owner is not null)
        {
            // Stash only — writing the model here would defeat the change-gated setter (it would see no change at
            // commit and skip the push). The model still holds the OLD value until Commit runs CommitWire.
            _pending = model;
            _hasPending = true;
            return;
        }

        if (_property is not null && _property.CanWrite)
        {
            _property.SetValue(_target, model);
            // A struct-in-struct member: re-box the mutated parent value and write it back up the chain to its
            // nearest reference owner (rare — nested types are usually reference types, mutated in place above).
            _valueTypeParent?.WriteBackValueType(_target);
            return;
        }

        if (_list is not null && _index >= 0 && _index < _list.Count)
            _list[_index] = model;
    }

    public void Commit()
    {
        // Nested accessors re-serialise the WHOLE top-level property through the root: CommitWire → ApplyField
        // reconstructs a fresh object via ReadBare, so the change-gated setter sees a different reference and
        // pushes. A top-level accessor commits its own pending value directly.
        if (_root is not null)
        {
            _root.Commit();
            return;
        }

        if (_owner is null || _wire is null)
            return;

        _owner.CommitWire(_wire, EngineSyncValue.WriteBare(Get()));
        // The model now holds the value the setter just applied, so stop shadowing it with the pending copy.
        _hasPending = false;
        _pending = null;
    }

    public IValueAccessor Member(PropertyDescriptor member)
    {
        var value = Get();
        // A child descriptor's Name is the member's snake_case wire name (how the walk emits DataProperties);
        // match it back to the live property by the same casing rule the codec keys on.
        var property = value?.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(candidate => candidate.Name.ToSnakeCase() == member.Name);
        if (property is null)
            // No live member (a null nested value): a read-only, empty cursor so the widget renders a blank.
            return new ReflectedAccessor(Root(), null, DummyProperty(member), null);

        // A value-type member of a value-type parent must write back up the chain; a reference-typed value is
        // mutated in place, so no write-back is needed.
        var valueTypeParent = value is not null && value.GetType().IsValueType ? this : null;
        return new ReflectedAccessor(Root(), value, property, valueTypeParent);
    }

    public IValueAccessor Element(int index)
    {
        var element = EngineSyncValue.ElementTypeOf(_valueType) ?? typeof(object);
        return Get() is IList list
            ? new ReflectedAccessor(Root(), list, index, element)
            : new ReflectedAccessor(Root(), null, DummyProperty(element), null);
    }

    public object? CreateElement()
    {
        var element = EngineSyncValue.ElementTypeOf(_valueType);
        if (element is null)
            return null;
        if (element == typeof(string))
            return string.Empty;
        return element.IsValueType || element.GetConstructor(Type.EmptyTypes) is not null
            ? Activator.CreateInstance(element)
            : null;
    }

    // The root top-level accessor this cursor's subtree commits through (itself when already top-level).
    private ReflectedAccessor Root() => _root ?? this;

    // Re-boxes a mutated value-type member back into this accessor's own slot, propagating a struct-in-struct edit
    // up toward the nearest reference owner. Only reached for the rare value-type parent chain.
    private void WriteBackValueType(object? mutated)
    {
        // A value-type field reached the TOP level: there is no property to write, so stash the mutated box as the
        // field's pending value (committed as one whole property, like any top-level edit).
        if (_owner is not null)
        {
            _pending = mutated;
            _hasPending = true;
            return;
        }

        if (_property is not null && _property.CanWrite)
        {
            _property.SetValue(_target, mutated);
            _valueTypeParent?.WriteBackValueType(_target);
        }
        else if (_list is not null && _index >= 0 && _index < _list.Count)
        {
            _list[_index] = mutated;
        }
    }

    // Presents an enum value to the widget layer as its snake_case choice string (the dropdown edits strings);
    // every other value passes through as its CLR form.
    private object? AsClrValue(object? model) =>
        _valueType.IsEnum && model is not null ? model.ToString()!.ToSnakeCase() : model;

    // The inverse of AsClrValue: an enum member's incoming snake_case string is parsed back to the enum value.
    private object? FromClrValue(object? value) =>
        _valueType.IsEnum && value is string text ? EngineSyncValue.ReadEnum(_valueType, new JValue(text)) : value;

    // A placeholder PropertyInfo for a member/element whose live parent value is null, so the empty cursor still
    // reports the declared type. Never read/written (the target is null).
    private static PropertyInfo DummyProperty(PropertyDescriptor member) => DummyProperty(ClrTypeFor(member));

    private static PropertyInfo DummyProperty(Type type) => new NullProperty(type);

    // The CLR type a null-member cursor should report, inferred from the descriptor's structural token (only used
    // to type an empty editor; the value itself is null).
    private static Type ClrTypeFor(PropertyDescriptor member) => member.Type switch
    {
        EngineTypes.Bool => typeof(bool),
        EngineTypes.Int => typeof(int),
        EngineTypes.Float => typeof(float),
        EngineTypes.Double => typeof(double),
        EngineTypes.String or EngineTypes.Enum => typeof(string),
        EngineTypes.Vec2 => typeof(System.Numerics.Vector2),
        EngineTypes.Vec3 => typeof(System.Numerics.Vector3),
        EngineTypes.Vec4 => typeof(System.Numerics.Vector4),
        EngineTypes.Quat => typeof(System.Numerics.Quaternion),
        EngineTypes.Color => typeof(Avalonia.Media.Color),
        EngineTypes.Handle or EngineTypes.Entity => typeof(Toybox.Studio.Project.AssetHandle),
        _ => typeof(object),
    };

    // A minimal read-only PropertyInfo standing in for a null member's declared type — its GetValue returns null
    // and SetValue is a no-op, so an empty nested cursor renders a blank without a live parent object.
    private sealed class NullProperty(Type type) : PropertyInfo
    {
        public override Type PropertyType => type;
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override PropertyAttributes Attributes => PropertyAttributes.None;
        public override string Name => "";
        public override Type? DeclaringType => null;
        public override Type? ReflectedType => null;

        public override object? GetValue(
            object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? index, System.Globalization.CultureInfo? culture) => null;

        public override void SetValue(
            object? obj, object? value, BindingFlags invokeAttr, Binder? binder, object?[]? index, System.Globalization.CultureInfo? culture)
        {
        }

        public override MethodInfo[] GetAccessors(bool nonPublic) => [];
        public override MethodInfo? GetGetMethod(bool nonPublic) => null;
        public override MethodInfo? GetSetMethod(bool nonPublic) => null;
        public override ParameterInfo[] GetIndexParameters() => [];
        public override object[] GetCustomAttributes(bool inherit) => [];
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => [];
        public override bool IsDefined(Type attributeType, bool inherit) => false;
    }
}
