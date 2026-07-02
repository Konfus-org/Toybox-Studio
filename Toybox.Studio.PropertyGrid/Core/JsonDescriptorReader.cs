using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Reads a describe/lean JSON body into <c>(PropertyDescriptor, JsonAccessor)</c> pairs — the slim JSON path the
/// grid keeps for engine-authored data Studio can't model in C# (plugin/unknown components, script-exposed
/// properties, the project-settings schema, the offline editor-settings POCO). The descriptor carries the shape,
/// the accessor the live value.
///
/// Leaf-vs-composite is decided from the self-describing shape rather than a hardcoded token list: a struct
/// serializes its fields as nested <c>{ type, value }</c> wrappers, while a leaf aggregate (color, vecN,
/// matN, quat) carries bare values — so "value is an object whose members are themselves typed wrappers"
/// (or the explicit "array" token) means composite, and everything else is a leaf. Plain JSON (settings
/// files, reflected POCOs) carries no type tags, so it falls back to inferring from the JSON shape.
///
/// The wire keys and type tokens come from the shared <see cref="EngineKeys"/>/<see cref="EngineTypes"/> vocabulary,
/// which the describe emitter writes with — the two must agree byte-for-byte. A std::variant property is tagged
/// with the <see cref="EngineTypes.Variant"/> token, its value being the variant's own { type, value } alternative.
/// Two node shapes are accepted: persisted/lean data is { "type", "value" } (type token and metadata at the top
/// level); the describe path (Entity::describe) reshapes each node to { "attributes": { "type", &lt;metadata&gt; },
/// "value" }. Settings files arrive lean; world.describe arrives attributed, so both must be decoded — see
/// <see cref="Unwrap"/>. The value side stays on <see cref="JsonAccessor"/>, which re-reads and unwraps the live
/// token itself when navigating children, so only the TOP-LEVEL value token plus the full descriptor tree are
/// built here.
/// </summary>
public static class JsonDescriptorReader
{
    // Aggregate leaf types whose value is a JSON object (e.g. a colour's { r, g, b, a }) rather than an
    // array. These are always edited by a single leaf widget (a colour picker), never a sub-grid — even when
    // the describe path expands their channels into typed { type, value } wrappers — so they're excluded from
    // the "members are typed ⇒ composite" rule below. (vecN/quat are array-valued and route through
    // IsCompositeArray, so they need no entry here.)
    private static readonly HashSet<string> ObjectLeafAggregates = new(StringComparer.Ordinal) { EngineTypes.Color };

    // Aggregate leaf types whose value is a JSON array of bare numbers (a fixed-size math type) rather than a
    // resizable list. These keep their own leaf widget (the vector/rotation editors) instead of expanding into
    // a sub-list — so they're the lone exceptions to "an array value is an editable list" below. Everything
    // else that serializes to an array IS a list, including a std::vector wrapped by a single-field container
    // struct (e.g. a material's parameters/textures bindings, whose struct token isn't the bare "array" one).
    private static readonly HashSet<string> ArrayLeafAggregates = new(StringComparer.Ordinal)
        { EngineTypes.Vec2, EngineTypes.Vec3, EngineTypes.Vec4, EngineTypes.Mat3, EngineTypes.Mat4, EngineTypes.Quat };

    /// <summary>
    /// Reads an object body into top-level descriptor+accessor pairs. Works for both the engine's typed JSON
    /// (each member is a <c>{ "type", "value" }</c> wrapper) and plain JSON (settings files / reflected POCOs),
    /// where tokens are inferred from the JSON shape. <paramref name="commitFor"/> maps a top-level property
    /// name to the commit its whole subtree shares (per-property engine push, or a buffered dirty), matching how
    /// the pre-reflection grid wired one commit per top-level property; null for a read-only grid.
    /// </summary>
    public static IReadOnlyList<(PropertyDescriptor Descriptor, IValueAccessor Accessor)> Read(
        JObject body, Func<string, Action?>? commitFor = null)
    {
        var built = new List<Built>(body.Count);
        foreach (var property in body.Properties())
        {
            // [[editor::hidden]] fields are dropped here, the single choke point feeding every grid
            // (inspector, app settings, editor settings), so they never become widgets.
            var node = Build(property.Name, Unwrap(property.Value));
            if (!node.Descriptor.Hidden)
                built.Add(node);
        }

        // Present fields in declaration (source) order, which the alphabetical JSON keys otherwise lose.
        // OrderBy is stable, so untyped data (no order attribute → all 0) keeps its original JSON order.
        return built
            .OrderBy(node => node.Descriptor.Order)
            .Select(node => (node.Descriptor, Accessor(node, commitFor?.Invoke(node.Descriptor.Name))))
            .ToList();
    }

    /// <summary>
    /// Reads one value token (a <c>{ type, value }</c> wrapper or a bare value) into a descriptor+accessor pair —
    /// for a value rendered outside a parsed object (a material slot's variant editor, an array element rebuild).
    /// The accessor references the live inner value token (no copy), so a leaf edit mutates it in place.
    /// </summary>
    public static (PropertyDescriptor Descriptor, IValueAccessor Accessor) ReadValue(
        string name, JToken? value, Action? commit)
    {
        var node = Build(name, Unwrap(value));
        return (node.Descriptor, Accessor(node, commit));
    }

    // The JsonAccessor for a built node: it owns the live top-level value token plus the two accessor-only
    // tokens (the resizable-list element template and the inline describe default). The accessor navigates its
    // own child subtree via Member/Element, so only the top-level token is handed to it here.
    private static IValueAccessor Accessor(Built node, Action? commit) =>
        new JsonAccessor(node.Value, node.Descriptor.Type, commit, node.ElementTemplate, node.Default);

    private static Built Build(string name, Wrapper wrapper)
    {
        var type = wrapper.Type;
        var value = wrapper.Value;

        if (value is JObject obj && IsComposite(type, obj))
            return Node(name, type is EngineTypes.Unknown or "" ? EngineTypes.Object : type, wrapper, BuildChildren(obj));

        if (value is JArray array && IsCompositeArray(type))
            return Node(name, EngineTypes.Array, wrapper, BuildElements(array));

        return Node(name, ResolveLeafType(wrapper), wrapper, children: null);
    }

    // The child descriptors of a struct/object: each member is itself a typed wrapper (or a bare value).
    // [[hidden]] members are dropped, then the set is source-ordered — mirroring the top-level Read.
    private static IReadOnlyList<PropertyDescriptor> BuildChildren(JObject obj)
    {
        var children = new List<Built>(obj.Count);
        foreach (var property in obj.Properties())
        {
            var child = Build(property.Name, Unwrap(property.Value));
            if (!child.Descriptor.Hidden)
                children.Add(child);
        }

        return children.OrderBy(node => node.Descriptor.Order).Select(node => node.Descriptor).ToList();
    }

    // The element descriptors of a composite array, labelled [0], [1], … — the element half of a list node.
    private static IReadOnlyList<PropertyDescriptor> BuildElements(JArray array)
    {
        var children = new List<PropertyDescriptor>(array.Count);
        for (var index = 0; index < array.Count; index++)
        {
            var child = Build($"[{index}]", Unwrap(array[index]));
            if (!child.Descriptor.Hidden)
                children.Add(child.Descriptor);
        }

        return children;
    }

    /// <summary>
    /// The widget token for a leaf value, disambiguated from its inline attribute metadata. Some
    /// semantically distinct types share a structural token, so the structural <see cref="Wrapper.Type"/>
    /// alone can't route them: a rotation quaternion arrives under the structural "vec4" token but is tagged
    /// with nested "quat" (→ the Euler editor), and an enum arrives under its own type-name token but carries
    /// the selectable <c>choices</c> (→ the dropdown). Everything else keeps its structural token, inferred
    /// from the JSON shape when the value is untyped.
    /// </summary>
    private static string ResolveLeafType(Wrapper wrapper)
    {
        // A handle's choices are its asset-type filter, not a dropdown's options — so a reference type
        // (handle/entity) keeps its own token and routes to its picker, with the choices flowing through as
        // the filter. Only a genuine enum turns its choices into a dropdown.
        if (wrapper.Type is EngineTypes.Handle or EngineTypes.Entity)
            return wrapper.Type;

        if (wrapper.Choices is { Count: > 0 })
            return EngineTypes.Enum;

        if (wrapper.Nested == EngineTypes.Quat)
            return EngineTypes.Quat;

        // Infer the widget from the JSON value for an untyped value (plain JSON), and also for a bare scalar
        // the engine could only tag structurally as "object" — an unsigned integer, or a choice-less enum
        // (one not marked [[serializable]], so it advertises no choices). Without this those settings would
        // fall through to the unknown-type placeholder instead of showing as a number field.
        var structural = wrapper.Type;
        if (structural == EngineTypes.Unknown
            || (structural == EngineTypes.Object && wrapper.Value is JValue { Type: not JTokenType.Null }))
            return InferToken(wrapper.Value);

        return structural;
    }

    /// <summary>
    /// An object value is composite (expands into a sub-grid) when it is plain JSON (no type tag), the
    /// explicit "object" token, or a struct — i.e. its members are themselves typed <c>{ type, value }</c>
    /// wrappers. Known object leaf aggregates (color) always stay a single widget regardless of member shape.
    /// </summary>
    private static bool IsComposite(string type, JObject value) =>
        !ObjectLeafAggregates.Contains(type)
        && (type is EngineTypes.Unknown or "" or EngineTypes.Object || value.Properties().Any(p => IsTypedWrapper(p.Value)));

    /// <summary>
    /// An array value is an editable list (expands into a sub-grid) unless it is a fixed-size math aggregate
    /// (vecN/matN/quat), which keeps its own leaf editor. This covers plain JSON arrays, the explicit "array"
    /// token (a std::vector), and a std::vector flattened under a single-field container struct's own type
    /// token (a material's parameters/textures bindings) — all of which the engine writes as a JSON array.
    /// </summary>
    private static bool IsCompositeArray(string type) => !ArrayLeafAggregates.Contains(type);

    private static bool IsTypedWrapper(JToken token) =>
        token is JObject obj && TryReadWrapper(obj, out _, out _);

    /// <summary>
    /// Splits a node into its metadata source object and value token, accepting both the attributed describe
    /// shape (<c>{ "attributes": { "type", … }, "value" }</c>) and the lean/persisted shape
    /// (<c>{ "type", "value", … }</c>). The metadata source is the object the type token and editor metadata
    /// keys are read from (the attributes sub-object, or the node itself). Returns false for a bare value.
    /// </summary>
    private static bool TryReadWrapper(JObject obj, out JObject metadata, out JToken value)
    {
        if (obj.TryGetValue(EngineKeys.Attributes, out var attributes)
            && attributes is JObject attributesObject
            && obj.TryGetValue(EngineKeys.Value, out var attributedValue))
        {
            metadata = attributesObject;
            value = attributedValue;
            return true;
        }

        if (obj.TryGetValue(EngineKeys.Type, out var typeToken)
            && typeToken.Type == JTokenType.String
            && obj.TryGetValue(EngineKeys.Value, out var leanValue))
        {
            metadata = obj;
            value = leanValue;
            return true;
        }

        metadata = obj;
        value = JValue.CreateNull();
        return false;
    }

    // Builds one node: the pure descriptor the grid binds, plus the live value + accessor-only tokens the pair's
    // JsonAccessor needs. The element-template descriptor is built from the template token (a nested descriptor);
    // the raw template + inline-default tokens flow to the accessor unchanged.
    private static Built Node(
        string name, string type, Wrapper wrapper, IReadOnlyList<PropertyDescriptor>? children)
    {
        var descriptor = new PropertyDescriptor
        {
            Name = name,
            Type = type,
            Category = wrapper.Category,
            Description = wrapper.Description,
            View = wrapper.View,
            Label = wrapper.Label,
            ReadOnly = wrapper.ReadOnly,
            Hidden = wrapper.Hidden,
            Order = wrapper.Order,
            // Icon/IconColor stay None/null: per-property type icons are editor-defined (via [Icon] on the C#
            // type), not carried by the describe metadata; the describe path carries none.
            Icon = Icon.None,
            IconColor = null,
            IsDefault = wrapper.IsDefault,
            // Choices are an asset-type filter (handle/entity) or enum options on a leaf. A struct drops
            // them, but a std::vector keeps them so the list widget can apply the filter to its elements
            // (e.g. a vector<Handle> with [[asset(...)]] renders each element as a filtered asset picker).
            Choices = children is null || type == EngineTypes.Array ? wrapper.Choices : null,
            // The resizable-list element template as a nested descriptor built from the template token.
            ElementTemplate = wrapper.ElementTemplate is { } template ? Build("element", Unwrap(template)).Descriptor : null,
            Children = children ?? [],
        };

        return new Built(descriptor, wrapper.Value, wrapper.ElementTemplate, wrapper.Default);
    }

    /// <summary>
    /// The single place the <c>{ "type", "value", metadata… }</c> convention is decoded. A "variant"-typed
    /// wrapper surfaces its active alternative. A bare value (plain JSON, or a missing token) returns type
    /// "unknown". The returned <see cref="Wrapper.Value"/> references the live inner token, so a leaf edit
    /// through the accessor mutates the backing document.
    /// </summary>
    private static Wrapper Unwrap(JToken? member)
    {
        if (member is JObject obj && TryReadWrapper(obj, out var metadata, out var value))
        {
            var type = metadata.Value<string>(EngineKeys.Type) ?? EngineTypes.Unknown;

            // A variant property surfaces as its active alternative: its value is itself a typed wrapper
            // (lean or attributed), so adopt the alternative's type/value while keeping the field's editor
            // metadata (category/description/etc. live on the outer wrapper).
            if (type == EngineTypes.Variant && value is JObject alternative)
            {
                var alt = Unwrap(alternative);
                if (alt.Type != EngineTypes.Unknown)
                {
                    type = alt.Type;
                    value = alt.Value;
                }
            }

            return new Wrapper(
                type,
                metadata.Value<string>(EngineKeys.Nested),
                value,
                metadata.Value<string>(EngineKeys.Category),
                metadata.Value<string>(EngineKeys.Description),
                metadata.Value<string>(EngineKeys.View),
                metadata.Value<string>(EngineKeys.Label),
                metadata.Value<bool?>(EngineKeys.ReadOnly) ?? false,
                metadata.Value<bool?>(EngineKeys.Hidden) ?? false,
                metadata.Value<int?>(EngineKeys.Order) ?? 0,
                ReadChoices(metadata[EngineKeys.Choices]),
                // is_default sits next to attributes/value on the node itself (describe-only), not inside
                // the attributes object.
                obj.Value<bool?>(EngineKeys.IsDefault) ?? false,
                // default likewise rides on the node itself, present only where the describe inlines one
                // (a script binding's override fields).
                obj[EngineKeys.Default],
                metadata[EngineKeys.ElementTemplate]);
        }

        return new Wrapper(
            EngineTypes.Unknown, null, member ?? JValue.CreateNull(), null, null, null, null, false, false, 0,
            null, false, null, null);
    }

    private static IReadOnlyList<string>? ReadChoices(JToken? token) =>
        token is JArray array
            ? array.Select(element => element.Value<string>() ?? string.Empty).ToList()
            : null;

    private static string InferToken(JToken value) => value.Type switch
    {
        JTokenType.Integer => EngineTypes.Int,
        JTokenType.Float => EngineTypes.Float,
        JTokenType.Boolean => EngineTypes.Bool,
        JTokenType.String => EngineTypes.String,
        JTokenType.Array => EngineTypes.Array,
        JTokenType.Object => EngineTypes.Object,
        _ => EngineTypes.Unknown,
    };

    // A built node: the pure descriptor the grid binds plus the accessor-side tokens (the live value token, the
    // resizable-list element template, and the inline describe default) its paired JsonAccessor is constructed
    // from. Kept internal to the reader so PropertyDescriptor stays a pure metadata shape.
    private readonly record struct Built(
        PropertyDescriptor Descriptor,
        JToken Value,
        JToken? ElementTemplate,
        JToken? Default);

    /// <summary>
    /// The pieces of a typed-JSON property wrapper, after splitting off the engine's metadata keys.
    /// </summary>
    private readonly record struct Wrapper(
        string Type,
        // The unwrapped semantic type name from the describe "nested" attribute (e.g. "quat" for a rotation
        // that shares the structural "vec4" token). Null on the lean/persisted path, which carries no
        // attributes. Lets the reader route such a value to the right widget — see ResolveLeafType.
        string? Nested,
        JToken Value,
        string? Category,
        string? Description,
        string? View,
        string? Label,
        bool ReadOnly,
        bool Hidden,
        int Order,
        IReadOnlyList<string>? Choices,
        bool IsDefault,
        // The describe-only inline default value, present only where the describe carries one (a script
        // binding's override fields). Null elsewhere.
        JToken? Default,
        // The describe-only "element_template" for a resizable list: the JSON of one default element,
        // which the list widget clones to append. Null for everything else.
        JToken? ElementTemplate);
}
