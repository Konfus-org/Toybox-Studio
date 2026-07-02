using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Avalonia.Media;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.Utils.Extensions;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Walks a typed <see cref="EngineSyncedObject"/> by reflection into <c>(PropertyDescriptor, IValueAccessor)</c>
/// pairs — the reflection-native replacement for laundering the model through <c>TypedDescribe</c> JSON and back.
/// It reproduces the same metadata logic (the C# editor attributes fold into the descriptor exactly as the engine
/// describe would emit them) but binds each field directly to the live model via a <see cref="ReflectedAccessor"/>,
/// so a grid edit drives the typed object and the reflection layer owns the RPC — no JSON round-trip.
///
/// <para>Only the descriptor TREE is produced here (a composite carries child descriptors); the child ACCESSORS
/// are produced lazily by <see cref="IValueAccessor.Member"/>/<see cref="IValueAccessor.Element"/> at grid-build
/// time, navigating the live object graph. So <see cref="ObjectPropertyViewModel"/> and the typed
/// <see cref="ArrayPropertyViewModel"/> get their child cursors from the accessor, not from the walk.</para>
/// </summary>
public static class ReflectionWalk
{
    private enum Kind { Leaf, Object, Array }

    /// <summary>
    /// Walks <paramref name="obj"/>'s <c>[EngineSync]</c> fields (the inherited chain included, so a data base —
    /// <c>Light</c>, <c>Trigger</c> — contributes its fields) into top-level descriptor+accessor pairs. A
    /// <c>[Hidden]</c> field is dropped; a read-only field's accessor is wrapped so its edits never persist. The
    /// per-field <c>is_default</c> is carried over from the object's last describe (<see cref="EngineSyncedObject.Raw"/>),
    /// since the engine is authoritative for "is this the default".
    /// </summary>
    public static IReadOnlyList<(PropertyDescriptor Descriptor, IValueAccessor Accessor)> Walk(EngineSyncedObject obj)
    {
        var pairs = new List<(PropertyDescriptor, IValueAccessor)>();
        foreach (var field in SyncedFields(obj.GetType()))
        {
            if (field.GetCustomAttribute<HiddenAttribute>() is not null)
                continue;

            var reflect = field.GetCustomAttribute<EngineSyncAttribute>()!;
            var wire = string.IsNullOrEmpty(reflect.Wire)
                ? ToProperty(field.Name).ToSnakeCase()
                : reflect.Wire!;
            var readOnly = field.GetCustomAttribute<ReadOnlyAttribute>() is not null
                || reflect.Mode is EngineSyncMode.ReadOnly or EngineSyncMode.Hydrate or EngineSyncMode.Stream;

            var isDefault = (obj.Raw[wire] as JObject)?.Value<bool?>(EngineKeys.IsDefault) ?? false;
            var descriptor = Describe(field.FieldType, field, wire, readOnly, isDefault);

            var property = obj.GetType().GetProperty(
                ToProperty(field.Name), BindingFlags.Public | BindingFlags.Instance);
            IValueAccessor accessor = property is not null
                ? ReflectedAccessor.ForField(obj, wire, property)
                : new JsonAccessor(null, descriptor.Type, null);
            if (readOnly)
                accessor = new ReadOnlyAccessor(accessor);

            pairs.Add((descriptor, accessor));
        }

        return pairs;
    }

    /// <summary>Whether <paramref name="type"/> has a typed C# model worth walking — i.e. it declares at least one
    /// <see cref="EngineSyncAttribute"/> field of its own or inherited. An <c>UnknownComponent</c>/<c>UnknownData</c>
    /// has none, so it stays on the JSON describe path.</summary>
    public static bool HasModel(Type type) => SyncedFields(type).Any();

    // Builds one property's descriptor: its structural token, editor metadata, choices, and (for a composite)
    // the recursively-built child descriptor tree. The wire name is the descriptor Name; is_default seeds the
    // "modified" indicator.
    private static PropertyDescriptor Describe(
        Type type, MemberInfo? member, string name, bool readOnly, bool isDefault, object? liveValue = null)
    {
        var (kind, token, element) = Classify(type);
        var metadata = ReadMetadata(member);

        IReadOnlyList<string>? choices = null;
        PropertyDescriptor? elementTemplate = null;
        IReadOnlyList<PropertyDescriptor> children = [];

        switch (kind)
        {
            case Kind.Object:
                token = EngineTypes.Object;
                // A string-keyed map describes by its live entries; a plain object/struct by its public properties.
                children = element is null
                    ? DescribeObjectChildren(type, liveValue, readOnly)
                    : DescribeMapChildren(element, liveValue, readOnly);
                break;

            case Kind.Array:
                token = EngineTypes.Array;
                children = DescribeArrayChildren(element!, liveValue, readOnly);
                // The element template (a default-element descriptor) so the list widget can append; and, for a
                // handle list, the element asset-type filter rides on the array node's own choices.
                elementTemplate = Describe(element!, null, "element", readOnly, isDefault: true);
                if (element == typeof(AssetHandle) && Extensions(member) is { } filter)
                    choices = filter;
                break;

            default:
                if (type.IsEnum)
                    // Choices must be the snake_case wire form WriteBare writes the value as, else the dropdown's
                    // value won't match any choice.
                    choices = Enum.GetNames(type).Select(entry => entry.ToSnakeCase()).ToList();
                else if (token == EngineTypes.Handle && Extensions(member) is { } assetFilter)
                    choices = assetFilter;
                break;
        }

        return new PropertyDescriptor
        {
            Name = name,
            Type = token,
            Choices = choices,
            Category = metadata.Category,
            Description = metadata.Description,
            View = metadata.View,
            Label = metadata.Label,
            ReadOnly = readOnly,
            Order = metadata.Order,
            // Icons are engine-blank on this path, matching the current describe path's behaviour.
            Icon = Icon.None,
            IconColor = null,
            IsDefault = isDefault,
            ElementTemplate = elementTemplate,
            Children = children,
        };
    }

    // A struct/object's child descriptors: one per public data property (skipping [Hidden]), keyed by snake_case
    // wire name — the same keys ReflectedAccessor.Member matches back to the live property.
    private static IReadOnlyList<PropertyDescriptor> DescribeObjectChildren(
        Type type, object? value, bool readOnly)
    {
        var children = new List<PropertyDescriptor>();
        foreach (var property in EngineSyncValue.DataProperties(type))
        {
            if (Attr<HiddenAttribute>(property) is not null)
                continue;

            var childReadOnly = readOnly || !property.CanWrite || Attr<ReadOnlyAttribute>(property) is not null;
            var childValue = value is null ? null : property.GetValue(value);
            children.Add(Describe(
                property.PropertyType, property, property.Name.ToSnakeCase(), childReadOnly, isDefault: false, childValue));
        }

        return children;
    }

    // A list's child descriptors — one per live element (so an object element carries its own sub-grid shape). An
    // empty/absent list yields no rows; the ArrayPropertyViewModel appends from the element template instead.
    private static IReadOnlyList<PropertyDescriptor> DescribeArrayChildren(
        Type element, object? value, bool readOnly)
    {
        var children = new List<PropertyDescriptor>();
        if (value is IEnumerable items)
        {
            var index = 0;
            foreach (var item in items)
                children.Add(Describe(element, null, $"[{index++}]", readOnly, isDefault: false, item));
        }

        return children;
    }

    // A string-keyed map's child descriptors: one per live entry, keyed by its (wire) key.
    private static IReadOnlyList<PropertyDescriptor> DescribeMapChildren(
        Type valueType, object? value, bool readOnly)
    {
        var children = new List<PropertyDescriptor>();
        if (value is IDictionary map)
            foreach (DictionaryEntry entry in map)
                children.Add(Describe(
                    valueType, null, entry.Key?.ToString() ?? string.Empty, readOnly, isDefault: false, entry.Value));

        return children;
    }

    private static (string? Label, string? Category, string? Description, int Order, string? View) ReadMetadata(
        MemberInfo? member)
    {
        if (member is null)
            return (null, null, null, 0, null);

        return (
            Attr<DisplayNameAttribute>(member)?.Label,
            Attr<CategoryAttribute>(member)?.Category,
            Attr<DescriptionAttribute>(member)?.Description,
            Attr<OrderAttribute>(member)?.Order ?? 0,
            // A [ViewModel(typeof(X))] routes by the editor type's full name; a [View("name")] routes by a
            // string name (used by data below the property grid, which can't reference an editor type).
            Attr<ViewModelAttribute>(member)?.ViewModel.FullName ?? Attr<ViewAttribute>(member)?.Name);
    }

    private static IReadOnlyList<string>? Extensions(MemberInfo? member) =>
        Attr<AssetExtensionsAttribute>(member)?.Extensions;

    // Reads an attribute from a member or, for a GENERATED property, its backing field — where the field-level
    // attributes (DisplayName, AssetExtensions) actually live.
    private static T? Attr<T>(MemberInfo? member) where T : Attribute =>
        member?.GetCustomAttribute<T>() ?? BackingField(member)?.GetCustomAttribute<T>();

    private static FieldInfo? BackingField(MemberInfo? member) =>
        member is PropertyInfo { Name.Length: > 0 } property && property.DeclaringType is { } owner
            ? owner.GetField(
                "_" + char.ToLowerInvariant(property.Name[0]) + property.Name[1..],
                BindingFlags.Instance | BindingFlags.NonPublic)
            : null;

    private static (Kind Kind, string Token, Type? Element) Classify(Type type)
    {
        // A raw JToken field (a variant payload kept verbatim) is a leaf; checked first so its own IEnumerable<JToken>
        // isn't mistaken for a list.
        if (typeof(JToken).IsAssignableFrom(type)) return (Kind.Leaf, EngineTypes.Unknown, null);
        if (type.IsEnum) return (Kind.Leaf, EngineTypes.Enum, null);
        if (type == typeof(bool)) return (Kind.Leaf, EngineTypes.Bool, null);
        if (type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong))
            return (Kind.Leaf, EngineTypes.Int, null);
        if (type == typeof(float)) return (Kind.Leaf, EngineTypes.Float, null);
        if (type == typeof(double)) return (Kind.Leaf, EngineTypes.Double, null);
        if (type == typeof(string)) return (Kind.Leaf, EngineTypes.String, null);
        if (type == typeof(Vector2)) return (Kind.Leaf, EngineTypes.Vec2, null);
        if (type == typeof(Vector3)) return (Kind.Leaf, EngineTypes.Vec3, null);
        if (type == typeof(Vector4)) return (Kind.Leaf, EngineTypes.Vec4, null);
        if (type == typeof(Quaternion)) return (Kind.Leaf, EngineTypes.Quat, null);
        if (type == typeof(Color)) return (Kind.Leaf, EngineTypes.Color, null);
        if (type == typeof(AssetHandle)) return (Kind.Leaf, EngineTypes.Handle, null);
        // A string-keyed map renders as a sub-object keyed by wire; checked before the list case (a dictionary is
        // enumerable).
        if (EngineSyncValue.DictionaryValueTypeOf(type) is { } mapValue) return (Kind.Object, EngineTypes.Object, mapValue);
        if (EngineSyncValue.ElementTypeOf(type) is { } element) return (Kind.Array, EngineTypes.Array, element);
        return (Kind.Object, EngineTypes.Object, null);
    }

    // Every [EngineSync] field up the inheritance chain (so a data base — Light/Trigger — is included), nearest
    // declaration first.
    private static IEnumerable<FieldInfo> SyncedFields(Type type)
    {
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            foreach (var field in current.GetFields(
                         BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                if (field.GetCustomAttribute<EngineSyncAttribute>() is not null)
                    yield return field;
    }

    // A backing field name → its public property name: strip a single leading underscore, capitalise the first.
    private static string ToProperty(string field)
    {
        var name = field.Length > 0 && field[0] == '_' ? field[1..] : field;
        return name.Length == 0 ? field : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
