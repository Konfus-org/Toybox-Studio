using System.Collections;
using System.Reflection;
using System.Text;
using IconPacks.Avalonia.Lucide;
using Toybox.Studio.PropertyGrid.Slots;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The CLR-object <see cref="IPropertyNodeFactory"/>: walks a target's public instance properties with
/// reflection and composes a node per property — a value editor + state indicator for the leaf shapes
/// (string, bool, numbers, enums), a child subtree per nested object, and a resizable list (add /
/// delete / drag-reorder per element) per <see cref="IList"/>. Leaf state ("differs from default",
/// reset) is fed by a parallel default instance of the target type when one can be constructed, so the
/// edited graph never knows about the grid. Writes go straight back through the property setters —
/// the host decides when the mutated graph commits (e.g. the settings' mutate-then-save). Value-type
/// composites and shapes without an editor render as read-only text. Domain leaf shapes the grid
/// doesn't know (a keybinding chord, …) plug in as <see cref="IValueEditor"/>s — a matching editor
/// wins over the built-ins and makes its type a leaf.
/// </summary>
public sealed class ReflectionPropertyNodeFactory : IPropertyNodeFactory
{
    // A runaway graph (self-referencing types the cycle guard can't see, e.g. through structs) stops
    // producing rows past this depth rather than hanging the grid.
    private const int MaxDepth = 8;

    private readonly IValueEditor[] _editors;

    public ReflectionPropertyNodeFactory(params IValueEditor[] editors) => _editors = editors;

    public IReadOnlyList<PropertyNode> CreateNodes(object target)
    {
        var path = new HashSet<object>(ReferenceEqualityComparer.Instance) { target };
        return CreateMemberNodes(target, DefaultsFor(target), depth: 0, path);
    }

    private IReadOnlyList<PropertyNode> CreateMemberNodes(
        object owner, object? defaults, int depth, HashSet<object> path)
    {
        if (depth > MaxDepth)
            return [];

        var nodes = new List<PropertyNode>();
        foreach (var property in owner.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead
                || property.GetIndexParameters().Length > 0
                || property.GetCustomAttribute<HiddenAttribute>() is not null)
            {
                continue;
            }

            if (CreateMemberNode(owner, property, defaults, depth, path) is { } node)
                nodes.Add(node);
        }

        // Plain rows read first, group bands (composites, lists) after — a leaf declared between two
        // groups must not render sandwiched between their bands. The sort is stable, so declaration
        // order holds within each half.
        return [.. nodes.OrderBy(node => node.IsHeader)];
    }

    /// <summary>The property's node, or null when it has nothing to show (a composite whose members are
    /// all hidden, an unresizable empty list) — an empty band would just be noise.</summary>
    private PropertyNode? CreateMemberNode(
        object owner, PropertyInfo property, object? defaults, int depth, HashSet<object> path)
    {
        var label = Humanize(property.Name);
        var editType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (IsLeaf(editType))
        {
            var accessor = CreateAccessor(owner, property, defaults);
            var node = new PropertyNode(label)
            {
                Value = CreateValueEditor(accessor, editType),
                Indicator = new StateIndicatorViewModel(accessor),
            };
            accessor.Changed += node.NotifyEdited;
            return node;
        }

        var value = property.GetValue(owner);
        if (value is null)
            return CreateReadOnlyTextNode(label, () => null);
        if (value is IList list)
            return CreateListNode(label, list, property.PropertyType, IconOf(property, value), depth, path);

        // Composites: recurse into reference types only — a boxed struct's members can't write back —
        // and only along an acyclic path. One with no member rows at all is skipped.
        if (!editType.IsValueType && path.Add(value))
        {
            var valueDefaults = defaults is not null ? property.GetValue(defaults) : null;
            var children = CreateMemberNodes(value, valueDefaults, depth + 1, path);
            path.Remove(value);
            return children.Count > 0
                ? new PropertyNode(label, children, IconOf(property, value))
                : null;
        }

        return CreateReadOnlyTextNode(label, () => property.GetValue(owner));
    }

    private PropertyNode? CreateListNode(
        string label, IList items, Type declaredType, PackIconLucideKind icon, int depth,
        HashSet<object> path)
    {
        var elementType = ElementTypeOf(declaredType);
        var resizable = items is { IsReadOnly: false, IsFixedSize: false } && CanCreateElement(elementType);

        // An empty list nobody can add to has nothing to show or do.
        if (!resizable && items.Count == 0)
            return null;

        // The add affordance needs the node its command mutates, which needs its slots first — the
        // local closes that loop.
        ListPropertyNode? node = null;
        node = new ListPropertyNode(
            label,
            items,
            createElementNode: list => CreateElementNode(list, items, elementType, resizable, depth, path),
            createElement: () => CreateElement(elementType),
            icon)
        {
            Right = resizable ? new AddItemViewModel(() => node!.AddNew()) : null,
        };
        return node;
    }

    /// <summary>
    /// Builds the row for the element at the tail of <paramref name="list"/>'s children. The row's
    /// accessors resolve the element's index through the list node at access time, so reorders never
    /// leave them pointing at a stale slot.
    /// </summary>
    private PropertyNode CreateElementNode(
        ListPropertyNode list, IList items, Type elementType, bool resizable, int depth, HashSet<object> path)
    {
        // The element's slots need the node they act on, which needs its slots first — the local
        // closes that loop.
        PropertyNode? node = null;
        var editType = Nullable.GetUnderlyingType(elementType) ?? elementType;
        var accessor = new PropertyValueAccessor(
            () => items[list.IndexOf(node!)],
            items.IsReadOnly ? null : value => items[list.IndexOf(node!)] = value);

        // The element being rowed is the one past the rows built so far (the caller appends in order).
        IEnumerable<PropertyNode>? children = null;
        object? editor = null;
        var value = items[list.Children.Count];
        if (IsLeaf(editType))
            editor = CreateValueEditor(accessor, editType);
        else if (value is not null && !editType.IsValueType && path.Add(value))
        {
            // An element that carries its own defaults (IDefaultSource) gives its member rows the
            // state adorner; a plain element has no parallel default instance to compare against.
            children = CreateMemberNodes(value, ElementDefaultsFor(value), depth + 1, path);
            path.Remove(value);
        }
        else
            editor = new TextValueViewModel(new PropertyValueAccessor(accessor.Get, set: null));

        // The label is a placeholder; the list node renumbers its elements after every mutation.
        node = new PropertyNode(string.Empty, children)
        {
            Left = resizable ? new ReorderHandleViewModel(() => list.IndexOf(node!), list.Move) : null,
            Value = editor,
            Right = resizable ? new DeleteItemViewModel(() => list.Remove(node!)) : null,
        };
        accessor.Changed += node.NotifyEdited;
        return node;
    }

    private static PropertyValueAccessor CreateAccessor(
        object owner, PropertyInfo property, object? defaults)
    {
        Func<object?> get = () => property.GetValue(owner);
        Action<object?>? set = property.SetMethod?.IsPublic is true
            ? value => property.SetValue(owner, value)
            : null;
        return defaults is not null
            ? new PropertyValueAccessor(get, set, property.GetValue(defaults))
            : new PropertyValueAccessor(get, set);
    }

    private ValueViewModel CreateValueEditor(PropertyValueAccessor accessor, Type editType)
    {
        foreach (var editor in _editors)
            if (editor.CanEdit(editType))
                return editor.CreateEditor(accessor, editType);

        return editType == typeof(bool) ? new BoolValueViewModel(accessor)
            : editType.IsEnum ? new EnumValueViewModel(accessor, editType)
            : NumericTypes.IsNumeric(editType) ? new NumberValueViewModel(accessor, editType)
            : new TextValueViewModel(accessor);
    }

    private static PropertyNode CreateReadOnlyTextNode(string label, Func<object?> get) =>
        new(label)
        {
            Value = new TextValueViewModel(new PropertyValueAccessor(
                () => get() is { } value ? value.ToString() : "(not set)", set: null)),
        };

    /// <summary>The header icon a member declares via [Icon] — the property's own wins over its
    /// type's; an unknown icon name renders none.</summary>
    private static PackIconLucideKind IconOf(PropertyInfo property, object value)
    {
        var name = property.GetCustomAttribute<IconAttribute>()?.Name
            ?? value.GetType().GetCustomAttribute<IconAttribute>()?.Name;
        return name is not null && Enum.TryParse<PackIconLucideKind>(name, ignoreCase: true, out var icon)
            ? icon
            : PackIconLucideKind.None;
    }

    private bool IsLeaf(Type type) =>
        type == typeof(string) || type == typeof(bool) || type.IsEnum || NumericTypes.IsNumeric(type)
        || _editors.Any(editor => editor.CanEdit(type));

    /// <summary>The list's element type, from its IEnumerable&lt;T&gt; shape; object when untyped.</summary>
    private static Type ElementTypeOf(Type declaredType) =>
        new[] { declaredType }.Concat(declaredType.GetInterfaces())
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GenericTypeArguments[0]
        ?? typeof(object);

    private static bool CanCreateElement(Type elementType) =>
        elementType == typeof(string)
        || elementType.IsValueType
        || elementType.GetConstructor(Type.EmptyTypes) is not null;

    private static object? CreateElement(Type elementType) =>
        elementType == typeof(string) ? string.Empty : Activator.CreateInstance(elementType);

    /// <summary>The parallel default instance a root target's rows compare against: the target's own
    /// (<see cref="IDefaultSource"/>) when it declares one, otherwise a default-constructed twin.</summary>
    private static object? DefaultsFor(object target) =>
        target is IDefaultSource source ? source.CreateDefaults() : TryCreateDefault(target.GetType());

    /// <summary>A list element's defaults come only from itself — every element of a list shares one
    /// type, so a default-constructed twin says nothing about THIS element's authored defaults.</summary>
    private static object? ElementDefaultsFor(object element) =>
        element is IDefaultSource source ? source.CreateDefaults() : null;

    private static object? TryCreateDefault(Type type)
    {
        try
        {
            return Activator.CreateInstance(type);
        }
        catch (Exception)
        {
            // No parameterless construction — rows simply carry no default/reset state.
            return null;
        }
    }

    /// <summary>PascalCase → spaced words ("HideEngineWindow" → "Hide Engine Window"); runs of capitals
    /// (acronyms) stay together.</summary>
    private static string Humanize(string name)
    {
        var text = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                text.Append(' ');
            text.Append(name[i]);
        }

        return text.ToString();
    }
}
