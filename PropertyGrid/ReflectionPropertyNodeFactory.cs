using Avalonia.Media;
using IconPacks.Avalonia.Lucide;
using System.Collections;
using System.Numerics;
using System.Reflection;
using System.Text;
using Toybox.Studio.PropertyGrid.Slots;
using Toybox.Studio.Utils.Attributes;
using Toybox.Studio.Utils.Composition;
using Toybox.Studio.Utils;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The CLR-object <see cref="IPropertyNodeFactory"/>: walks a target's public instance properties with
/// reflection and composes a node per property — a value editor + state indicator for the leaf shapes
/// (string, bool, numbers, enums), a child subtree per nested object, and a resizable list (add /
/// delete / drag-reorder per element) per <see cref="IList"/>. Leaf state ("differs from default",
/// reset) is fed by a parallel default instance of the target type when one can be constructed, so the
/// edited graph never knows about the grid. Value-type composites and shapes without an editor render
/// as read-only text. Domain leaf shapes the grid doesn't know (a keybinding chord, …) plug in as
/// <see cref="IValueEditor"/>s — a matching editor wins over the built-ins and makes its type a leaf.
///
/// Writes go straight back through the property setters, so the host decides when the mutated graph
/// commits (the settings' mutate-then-save). The one exception is immutable graphs: an immutable
/// <c>record</c> composite and a settable list property are edited <b>copy-on-write</b> — a member edit
/// rebuilds the record (or the list) and reassigns it through the owner's setter, rather than mutating
/// the instance in place. That is what makes the grid drive an engine-mirrored asset whose nested
/// records only push on reassignment: the reassignment is the edit the sync layer sees.
/// </summary>
public sealed class ReflectionPropertyNodeFactory : IPropertyNodeFactory
{
    // A runaway graph (self-referencing types the cycle guard can't see, e.g. through structs) stops
    // producing rows past this depth rather than hanging the grid.
    private const int MaxDepth = 8;

    // Records carry this compiler-generated public clone method; plain classes do not. It is both how we
    // recognize a record (immutable, copy-on-write) and how we copy one for a member edit.
    private const string CloneMethodName = "<Clone>$";

    private readonly ViewModelFactory _viewModels;
    private readonly IValueEditor[] _editors;

    public ReflectionPropertyNodeFactory(ViewModelFactory viewModels, params IValueEditor[] editors)
    {
        _viewModels = viewModels;
        _editors = editors;
    }

    public IReadOnlyList<PropertyNode> CreateNodes(object target)
    {
        var path = new HashSet<object>(ReferenceEqualityComparer.Instance) { target };
        return CreateMemberNodes(() => target, writeBack: null, DefaultTwin(target), depth: 0, path);
    }

    /// <param name="owner">Reads the owner instance at access time — for a copy-on-write record this is
    /// the latest rebuilt instance, so sequential edits stack.</param>
    /// <param name="writeBack">Non-null when the owner is an immutable record edited copy-on-write: a
    /// member write rebuilds the record and reassigns it here. Null means the owner is edited in place
    /// (the root, a mutable class).</param>
    private IReadOnlyList<PropertyNode> CreateMemberNodes(
        Func<object> owner, Action<object>? writeBack, object? defaults, int depth, HashSet<object> path)
    {
        if (depth > MaxDepth)
            return [];

        var nodes = new List<PropertyNode>();
        foreach (var property in owner().GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead
                || property.GetIndexParameters().Length > 0
                || property.GetCustomAttribute<HiddenAttribute>() is not null)
            {
                continue;
            }

            if (CreateMemberNode(owner, writeBack, property, defaults, depth, path) is { } node)
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
        Func<object> owner, Action<object>? writeBack, PropertyInfo property, object? defaults, int depth,
        HashSet<object> path)
    {
        var label = Humanize(property.Name);
        var category = property.GetCustomAttribute<CategoryAttribute>()?.Name;
        var editType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var write = MemberWriter(owner, writeBack, property);

        if (IsLeaf(editType))
        {
            var accessor = defaults is not null
                ? new PropertyValueAccessor(() => property.GetValue(owner()), write, property.GetValue(defaults))
                : new PropertyValueAccessor(() => property.GetValue(owner()), write);
            var node = new PropertyNode(label)
            {
                Value = CreateValueEditor(accessor, editType, property),
                Indicator = _viewModels.Create<StateIndicatorViewModel>(accessor),
            };
            accessor.Changed += node.NotifyEdited;
            return Categorized(node, category);
        }

        var value = property.GetValue(owner());
        if (value is null)
            return Categorized(CreateReadOnlyTextNode(label, () => null), category);
        if (value is IList list)
        {
            return Categorized(
                CreateListNode(label, list, write, property.PropertyType, IconOf(property, value), depth, path),
                category);
        }

        // Composites: recurse into reference types only — a boxed struct's members can't write back —
        // and only along an acyclic path. One with no member rows at all is skipped. An immutable record
        // is edited copy-on-write (rebuild + reassign through 'write'); a mutable class in place.
        if (!editType.IsValueType && path.Add(value))
        {
            var valueDefaults = defaults is not null ? property.GetValue(defaults) : null;
            Action<object>? childWriteBack =
                write is not null && IsRecord(value.GetType()) ? rebuilt => write(rebuilt) : null;
            var children = CreateMemberNodes(
                () => property.GetValue(owner())!, childWriteBack, valueDefaults, depth + 1, path);
            path.Remove(value);
            return children.Count > 0
                ? Categorized(new PropertyNode(label, children, IconOf(property, value)), category)
                : null;
        }

        // A shape the grid has no strategy for (a boxed struct without an editor, a cyclic reference): the
        // "?" indicator marks it unsupported and the value is edited as raw, validated JSON rather than shown
        // read-only. A property with no setter still surfaces its JSON, disabled.
        return Categorized(CreateUnknownNode(label, () => property.GetValue(owner()), write, editType), category);
    }

    /// <summary>Tags a node with its property's category (a no-op passthrough when there is no node). Only the
    /// grid's root nodes are grouped by it; the tag on a nested node is harmless.</summary>
    private static PropertyNode? Categorized(PropertyNode? node, string? category)
    {
        if (node is not null)
            node.Category = category;
        return node;
    }

    private PropertyNode CreateUnknownNode(
        string label, Func<object?> get, Action<object?>? write, Type valueType)
    {
        var accessor = new PropertyValueAccessor(get, write);
        var node = new PropertyNode(label)
        {
            Value = _viewModels.Create<UnknownValueViewModel>(accessor, valueType),
            Indicator = _viewModels.Create<UnknownIndicatorViewModel>(valueType),
        };
        accessor.Changed += node.NotifyEdited;
        return node;
    }

    private PropertyNode? CreateListNode(
        string label, IList items, Action<object?>? writeList, Type declaredType, PackIconLucideKind icon,
        int depth, HashSet<object> path)
    {
        var elementType = ElementTypeOf(declaredType);

        // A settable list is edited copy-on-write: the node works on a private copy and every mutation
        // reassigns a fresh copy through the property, so a synced/record owner sees a real reassignment
        // and pushes. Only when a fresh List is assignable back to the declared type; otherwise (a fixed
        // array, a getter-only list) the node mutates the instance the object exposes, in place.
        var canReassign = writeList is not null
            && declaredType.IsAssignableFrom(typeof(List<>).MakeGenericType(elementType));
        var working = canReassign ? CopyList(items, elementType) : items;
        var resizable = working is { IsReadOnly: false, IsFixedSize: false } && CanCreateElement(elementType);

        // An empty list nobody can add to has nothing to show or do.
        if (!resizable && working.Count == 0)
            return null;

        // The add affordance needs the node its command mutates, which needs its slots first — the
        // local closes that loop.
        ListPropertyNode? node = null;
        node = new ListPropertyNode(
            label,
            working,
            createElementNode: list => CreateElementNode(list, working, elementType, resizable, depth, path),
            createElement: () => CreateElement(elementType),
            icon)
        {
            Right = resizable ? new AddItemViewModel(() => node!.AddNew()) : null,
        };

        // Every mutation (add / delete / reorder / element edit bubbles here) reassigns a fresh snapshot.
        if (canReassign)
            node.Edited += () => writeList!(CopyList(working, elementType));

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
            // Each element's default twin comes from itself (reconstructed from its own constructor), so
            // an element whose defaults are per-instance (a keybinding's registered chord) gives its member
            // rows the state adorner. Element members write in place — the list reassign above carries the
            // whole element to the engine.
            children = CreateMemberNodes(() => value, writeBack: null, DefaultTwin(value), depth + 1, path);
            path.Remove(value);
        }
        else
            editor = _viewModels.Create<TextValueViewModel>(new PropertyValueAccessor(accessor.Get, set: null));

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

    /// <summary>
    /// The write path onto <paramref name="owner"/>'s <paramref name="property"/>: null when read-only,
    /// a direct setter when the owner is edited in place, or a copy-on-write rebuild-and-reassign when
    /// the owner is an immutable record (<paramref name="writeBack"/> lands the rebuilt record).
    /// </summary>
    private static Action<object?>? MemberWriter(
        Func<object> owner, Action<object>? writeBack, PropertyInfo property)
    {
        if (property.SetMethod?.IsPublic is not true)
            return null;

        return writeBack is null
            ? value => property.SetValue(owner(), value)
            : value =>
            {
                var copy = Clone(owner());
                property.SetValue(copy, value);
                writeBack(copy);
            };
    }

    private ValueViewModel CreateValueEditor(
        PropertyValueAccessor accessor, Type editType, PropertyInfo? property = null)
    {
        foreach (var editor in _editors)
            if (editor.CanEdit(editType))
                return editor.CreateEditor(accessor, editType, property);

        // A [Slider(min, max)] numeric property renders as a bounded track instead of the spinner; the
        // attribute is only honored on a real property (list elements have none) and only on numbers.
        if (property?.GetCustomAttribute<SliderAttribute>() is { } slider && NumericTypes.IsNumeric(editType))
            return _viewModels.Create<SliderValueViewModel>(accessor, editType, slider.Minimum, slider.Maximum);

        if (editType == typeof(Color))
            return _viewModels.Create<ColorValueViewModel>(accessor);
        if (editType == typeof(Quaternion))
            return _viewModels.Create<RotationValueViewModel>(accessor);
        if (VectorComponentCount(editType) is { } components)
            return _viewModels.Create<VectorValueViewModel>(accessor, components);

        return editType == typeof(bool) ? _viewModels.Create<BoolValueViewModel>(accessor)
            : editType == typeof(RegexPattern) ? _viewModels.Create<RegexValueViewModel>(accessor)
            : editType.IsEnum ? _viewModels.Create<EnumValueViewModel>(accessor, editType)
            : NumericTypes.IsNumeric(editType) ? _viewModels.Create<NumberValueViewModel>(accessor, editType)
            : _viewModels.Create<TextValueViewModel>(accessor);
    }

    private PropertyNode CreateReadOnlyTextNode(string label, Func<object?> get) =>
        new(label)
        {
            Value = _viewModels.Create<TextValueViewModel>(new PropertyValueAccessor(
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
        type == typeof(string) || type == typeof(bool) || type == typeof(RegexPattern)
        || type.IsEnum || NumericTypes.IsNumeric(type) || IsStructuredLeaf(type)
        || _editors.Any(editor => editor.CanEdit(type));

    /// <summary>The value-type leaves the grid edits with a built-in composite editor (a colour swatch,
    /// vector fields, a Euler rotation) — value types the composite recursion would otherwise skip.</summary>
    private static bool IsStructuredLeaf(Type type) =>
        type == typeof(Color) || type == typeof(Quaternion) || VectorComponentCount(type) is not null;

    /// <summary>The component count of a <c>Vector2/3/4</c>, or null when the type isn't one.</summary>
    private static int? VectorComponentCount(Type type) =>
        type == typeof(Vector2) ? 2 : type == typeof(Vector3) ? 3 : type == typeof(Vector4) ? 4 : null;

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

    private static IList CopyList(IList source, Type elementType)
    {
        var copy = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        foreach (var item in source)
            copy.Add(item);
        return copy;
    }

    /// <summary>A record type carries a compiler-generated public clone method; a plain class does not.</summary>
    private static bool IsRecord(Type type) => CloneOf(type) is not null;

    /// <summary>A shallow copy of a record, for a copy-on-write member edit.</summary>
    private static object Clone(object instance) => CloneOf(instance.GetType())!.Invoke(instance, null)!;

    private static MethodInfo? CloneOf(Type type) =>
        type.GetMethod(CloneMethodName, BindingFlags.Instance | BindingFlags.Public);

    /// <summary>
    /// The parallel "default" instance a value's rows compare against and reset to — the premise being
    /// that a freshly constructed instance <b>is</b> the default. It is rebuilt from the instance's own
    /// least-arg constructor, sourcing each parameter from the same-named readable property (the identity
    /// and per-instance defaults the instance already carries, e.g. a keybinding's registered chord),
    /// while settable state falls to whatever that constructor assigns. Value types get <c>default</c>;
    /// anything not constructible this way carries no default/reset state (null).
    /// </summary>
    private static object? DefaultTwin(object instance)
    {
        var type = instance.GetType();
        try
        {
            if (type.IsValueType)
                return Activator.CreateInstance(type);

            var ctor = type.GetConstructors()
                .OrderBy(c => c.GetParameters().Length)
                .FirstOrDefault();
            if (ctor is null)
                return null;

            var arguments = Array.ConvertAll(ctor.GetParameters(), parameter =>
            {
                var source = parameter.Name is { } name
                    ? type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                    : null;
                return source is { CanRead: true } ? source.GetValue(instance)
                    : parameter.IsOptional ? parameter.DefaultValue
                    : null;
            });
            return ctor.Invoke(arguments);
        }
        catch (Exception)
        {
            // Not reconstructible — rows simply carry no default/reset state.
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
