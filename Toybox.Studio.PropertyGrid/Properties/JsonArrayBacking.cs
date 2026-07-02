using System.Linq;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The JSON backing for an <see cref="ArrayPropertyViewModel"/> — a live <see cref="JArray"/> inside a describe
/// body, mutated in place. Preserves the pre-reflection behaviour engine-authored data depends on: elements are
/// keyed by their live token reference (stable across add/remove/reorder), a <c>[[hidden]]</c> element is dropped
/// from the visible rows without shifting the others, a struct element gets a meaningful header from a "name" or
/// handle child, and a <c>vector&lt;Handle&gt;</c>'s scalar elements are retyped to filtered asset pickers.
/// </summary>
internal sealed class JsonArrayBacking : IArrayBacking
{
    private readonly JArray? _array;
    private readonly JToken? _elementTemplate;
    private readonly IReadOnlyList<string>? _elementChoices;
    private readonly bool _resizable;
    private readonly Action _commit;

    public JsonArrayBacking(PropertyDescriptor descriptor, IValueAccessor accessor, Action commit)
    {
        _array = accessor.Get() as JArray;
        _elementTemplate = accessor.CreateElement() as JToken;
        _elementChoices = descriptor.Choices;
        _resizable = _array is not null && _elementTemplate is not null && accessor.CanWrite;
        _commit = commit;
    }

    public bool IsResizable => _resizable;

    public string ElementType => _elementTemplate is null
        ? ""
        : JsonDescriptorReader.ReadValue("element", _elementTemplate, null).Descriptor.Type;

    public IReadOnlyList<(object Key, PropertyDescriptor Descriptor, IValueAccessor Accessor)> VisibleElements()
    {
        var elements = new List<(object, PropertyDescriptor, IValueAccessor)>();
        if (_array is null)
            return elements;

        for (var index = 0; index < _array.Count; index++)
        {
            var token = _array[index];
            var (descriptor, accessor) = JsonDescriptorReader.ReadValue($"[{index}]", token, _commit);
            if (descriptor.Hidden)
                continue;

            var (headered, elementAccessor) = AsHandleElement(Headered(descriptor, accessor), accessor);
            elements.Add((token, headered, elementAccessor));
        }

        return elements;
    }

    public void Add()
    {
        if (_array is not null && _elementTemplate is not null)
            _array.Add(_elementTemplate.DeepClone());
    }

    public void Remove(object key)
    {
        if (key is JToken token)
            token.Remove();
    }

    public void Duplicate(object key)
    {
        if (_array is not null && key is JToken token)
            _array.Insert(_array.IndexOf(token) + 1, token.DeepClone());
    }

    public void Move(object key, object target, bool after)
    {
        if (_array is null || key is not JToken token || target is not JToken targetToken)
            return;

        token.Remove();
        // Insert relative to the target's live position (recomputed after the removal).
        _array.Insert(_array.IndexOf(targetToken) + (after ? 1 : 0), token);
    }

    public void AppendValue(JToken value) => _array?.Add(value.DeepClone());

    public bool ResetTo(JToken token)
    {
        if (_array is null || token is not JArray defaults)
            return false;

        _array.Clear();
        foreach (var element in defaults)
            _array.Add(element.DeepClone());
        return true;
    }

    // Retypes a scalar element of an asset-filtered list (a vector<Handle> carrying [[asset(...)]] choices) as a
    // "handle" so the factory builds a filtered asset picker for it instead of a raw number field — the picker
    // reads the id as a ulong, so a high-bit handle id round-trips without the precision loss a JSON number field
    // would inflict. Non-asset lists and non-scalar elements are returned unchanged.
    private (PropertyDescriptor Descriptor, IValueAccessor Accessor) AsHandleElement(
        PropertyDescriptor element, IValueAccessor accessor)
    {
        if (_elementChoices is not { Count: > 0 }
            || element.Type == EngineTypes.Handle
            || accessor.Get() is not JValue { Type: JTokenType.Integer })
            return (element, accessor);

        var descriptor = Clone(element, element.Label, EngineTypes.Handle, _elementChoices);
        // The value is the same live integer token; re-read it through a handle-wired accessor so the picker
        // reads/writes it as an AssetHandle id.
        return (descriptor, new JsonAccessor((JToken)accessor.Get()!, EngineTypes.Handle, _commit));
    }

    // A struct/object element shows a meaningful header instead of its "[i]" index when it carries an obvious
    // identity: a child literally named "name" (a string), or failing that the first handle child resolved to
    // its asset name. Anything else keeps the index.
    private static PropertyDescriptor Headered(PropertyDescriptor element, IValueAccessor accessor)
    {
        if (element.Children.Count == 0)
            return element;

        var label = DeriveHeader(element, accessor);
        return label is null ? element : Clone(element, label, element.Type, element.Choices);
    }

    private static string? DeriveHeader(PropertyDescriptor element, IValueAccessor accessor)
    {
        var nameChild = element.Children.FirstOrDefault(
            child => string.Equals(child.Name, "name", StringComparison.OrdinalIgnoreCase));
        if (nameChild is { Type: EngineTypes.String }
            && accessor.Member(nameChild).Get() is string { Length: > 0 } text
            && !string.IsNullOrWhiteSpace(text))
            return text;

        var handleChild = element.Children.FirstOrDefault(child => child.Type == EngineTypes.Handle);
        if (handleChild is not null && AssetGridServices.Assets is { } assets)
        {
            // Handle ids are unsigned 64-bit, matching the catalog's key.
            var id = (accessor.Member(handleChild).Get() as AssetHandle?)?.Id ?? 0;
            var resolved = id == 0 ? null : assets.ResolveName(id);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        return null;
    }

    // PropertyDescriptor is init-only, so an override (a header label, or a handle retype) is a clone with the
    // changed fields.
    private static PropertyDescriptor Clone(
        PropertyDescriptor descriptor, string? label, string type, IReadOnlyList<string>? choices) => new()
    {
        Name = descriptor.Name,
        Type = type,
        Choices = choices,
        Category = descriptor.Category,
        Description = descriptor.Description,
        ReadOnly = descriptor.ReadOnly,
        Hidden = descriptor.Hidden,
        Order = descriptor.Order,
        View = descriptor.View,
        Label = label,
        Icon = descriptor.Icon,
        IconColor = descriptor.IconColor,
        IsDefault = descriptor.IsDefault,
        ElementTemplate = descriptor.ElementTemplate,
        Children = descriptor.Children,
    };
}
