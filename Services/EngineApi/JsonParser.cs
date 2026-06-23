using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.World;

namespace Toybox.Studio.Services.EngineApi;

/// <summary>
/// Parses the engine's self-describing JSON into the editor's UI-ready models. It owns two related jobs:
/// turning a raw world.describe reply (entities + their components) into an <see cref="Entity"/> hierarchy,
/// and turning any typed-JSON object — every property written as
/// <c>{ "type": &lt;token&gt;, "value": &lt;value&gt; }</c> — into a <see cref="PropertyNode"/> tree the
/// property grid can render.
///
/// Leaf-vs-composite is decided from the self-describing shape rather than a hardcoded token list: a struct
/// serializes its fields as nested <c>{ type, value }</c> wrappers, while a leaf aggregate (color, vecN,
/// matN, quat) carries bare values — so "value is an object whose members are themselves typed wrappers"
/// (or the explicit "array" token) means composite, and everything else is a leaf. Plain JSON (settings
/// files, reflected POCOs) carries no type tags, so it falls back to inferring from the JSON shape.
/// </summary>
public sealed class JsonParser
{
    // Mirror the engine's PROPERTY_*_KEY (serialization.h). A std::variant property is tagged with the
    // "variant" token, its value being the variant's own { type, value } alternative.
    //
    // Two node shapes are accepted. Persisted/lean data is { "type", "value" } (with the type token and any
    // metadata at the top level). The describe path (Entity::describe) reshapes each node to
    // { "attributes": { "type", <metadata> }, "value" }, gathering the type token and all reflection
    // metadata into one sub-object. Settings files arrive lean; world.describe arrives attributed, so both
    // must be decoded — see Unwrap.
    private const string TypeKey = "type";
    private const string ValueKey = "value";
    private const string NestedKey = "nested";
    private const string AttributesKey = "attributes";
    private const string CategoryKey = "category";
    private const string DescriptionKey = "description";
    private const string ChoicesKey = "choices";
    private const string ViewKey = "view";
    private const string LabelKey = "label";
    private const string ReadOnlyKey = "readonly";
    private const string HiddenKey = "hidden";
    private const string IconKey = "icon";
    private const string IconColorKey = "iconColor";
    private const string IsDefaultKey = "is_default";
    private const string OrderKey = "order";
    private const string ElementTemplateKey = "element_template";
    private const string VariantToken = "variant";
    private const string ArrayToken = "array";
    private const string ObjectToken = "object";
    private const string UnknownToken = "unknown";

    // Aggregate leaf types whose value is a JSON object (e.g. a colour's { r, g, b, a }) rather than an
    // array. These are always edited by a single leaf widget (a colour picker), never a sub-grid — even when
    // the describe path expands their channels into typed { type, value } wrappers — so they're excluded from
    // the "members are typed ⇒ composite" rule below. (vecN/quat are array-valued and route through
    // IsCompositeArray, so they need no entry here.)
    private static readonly HashSet<string> ObjectLeafAggregates = new(StringComparer.Ordinal) { "color" };

    /// <summary>
    /// Flattens a raw world.describe reply into a sorted entity hierarchy, parsing each entity's components
    /// into their property trees along the way. Pure and CPU-bound — callers run it off the UI thread. Type
    /// disambiguation (e.g. routing a rotation quaternion to the Euler editor) comes from each property's
    /// inline attribute metadata, which world.describe already carries — no separate reflection lookup.
    /// </summary>
    public IReadOnlyList<EntityDescription> ParseWorld(JObject reply)
    {
        var componentTypes = reply["component_types"] as JObject;
        var entities = reply["entities"] as JArray ?? [];

        var nodes = new Dictionary<ulong, (EntityDescription Node, ulong Parent)>();
        foreach (var token in entities)
        {
            if (token is not JObject entity)
                continue;

            var node = BuildEntity(entity, componentTypes);
            nodes[node.Id] = (node, Unwrap(entity["parent"]).Value.Value<ulong>());
        }

        var roots = new List<EntityDescription>();
        foreach (var (node, parent) in nodes.Values)
        {
            if (parent != 0 && nodes.TryGetValue(parent, out var owner))
                owner.Node.Children.Add(node);
            else
                roots.Add(node);
        }

        SortRecursively(roots);
        return roots;
    }

    /// <summary>
    /// Parses one entity from an <c>entity.describe</c> reply (<c>{ entity, component_types }</c>) into the
    /// UI snapshot model. Children are not resolved (the tree owns parenting); used to refresh a single
    /// selected entity's live component values without re-describing the whole world. Returns null if the
    /// reply has no entity.
    /// </summary>
    public EntityDescription? ParseEntity(JObject reply)
    {
        if (reply["entity"] is not JObject entity)
            return null;

        return BuildEntity(entity, reply["component_types"] as JObject);
    }

    /// <summary>
    /// Parses an object into a property tree. Works for both the engine's typed JSON (each member is a
    /// <c>{ "type", "value" }</c> wrapper) and plain JSON (settings files / reflected POCOs), where tokens
    /// are inferred from the JSON shape. Composite values recurse so nested structs/objects render as
    /// sub-grids either way.
    /// </summary>
    public IReadOnlyList<PropertyNode> ParseProperties(JObject json) => ParsePropertiesCore(json);

    /// <summary>
    /// Parses a JSON array's elements into property nodes, labelled <c>[0]</c>, <c>[1]</c>, … This is the
    /// element half of a composite array node; the editable list widget reuses it to rebuild its child
    /// view-models after an add/remove/reorder mutates the backing array.
    /// </summary>
    /// <summary>
    /// Builds a single property node from one typed value token (a <c>{ type, value }</c> / attributed
    /// wrapper, or a bare value), named <paramref name="name"/>. Used to render an individual value with the
    /// type-driven widget when it doesn't come from a parsed object — e.g. a material instance's per-slot
    /// override editor. The node's <see cref="PropertyNode.Value"/> references the live token (no copy), so
    /// the widget mutates it in place.
    /// </summary>
    public static PropertyNode ParseValueNode(string name, JToken? value) => Build(name, Unwrap(value));

    public static IReadOnlyList<PropertyNode> ParseArrayElements(JArray array)
    {
        var children = new List<PropertyNode>(array.Count);
        for (var index = 0; index < array.Count; index++)
        {
            // An element may itself be a typed wrapper (e.g. a struct field) or a bare value.
            var node = Build($"[{index}]", Unwrap(array[index]));
            if (!node.Hidden)
                children.Add(node);
        }

        return children;
    }

    // Builds the UI snapshot for a single serialized entity (id/name/tag/order + parsed components). Shared
    // by the whole-world parse and the single-entity refresh.
    private EntityDescription BuildEntity(JObject entity, JObject? componentTypes)
    {
        var id = Unwrap(entity["id"]).Value.Value<ulong>();
        var name = Unwrap(entity["name"]).Value.Value<string>();
        return new EntityDescription
        {
            Id = id,
            Name = string.IsNullOrEmpty(name) ? $"Entity {id}" : name,
            // "tags" is a typed array of strings (the entity's serialized gameplay tags).
            Tags = (Unwrap(entity["tags"]).Value as JArray)?
                .Select(token => token.Value<string>() ?? "")
                .Where(tag => tag.Length > 0)
                .ToList() ?? [],
            Order = Unwrap(entity["order"]).Value.Value<int>(),
            // is_global is a plain describe-only flag at the envelope root (not a typed {type,value} field).
            IsGlobal = entity.Value<bool?>("is_global") ?? false,
            // is_enabled is a typed {type,value} envelope scalar like name/order; absent/null ⇒ enabled.
            IsEnabled = Unwrap(entity["is_enabled"]).Value.Value<bool?>() ?? true,
            Components = ParseComponents(
                entity["components"] as JObject, componentTypes, entity["component_order"] as JArray),
        };
    }

    private List<ComponentDescription> ParseComponents(JObject? components, JObject? componentTypes, JArray? order)
    {
        var result = new List<ComponentDescription>();
        if (components is null)
            return result;

        foreach (var property in components.Properties())
        {
            if (property.Value is not JObject raw)
                continue;

            string? icon = null, iconColor = null;
            if (componentTypes?[property.Name] is JObject info)
                (icon, iconColor) = (info.Value<string>(IconKey), info.Value<string>(IconColorKey));

            result.Add(new ComponentDescription(property.Name, raw, ParseProperties(raw), icon, iconColor));
        }

        // Present components in the engine's registration order (component_order), not the alphabetical key
        // order the JSON map imposes. Any component missing from the list (or no list at all, e.g. settings)
        // sorts after, keeping a stable result.
        if (order is { Count: > 0 })
        {
            var rank = order
                .Select((token, index) => (Name: token.Value<string>() ?? "", Index: index))
                .ToDictionary(entry => entry.Name, entry => entry.Index);
            result = result
                .OrderBy(component => rank.TryGetValue(component.Name, out var index) ? index : int.MaxValue)
                .ToList();
        }

        return result;
    }

    // Entities sort by their explicit engine order (the user's arrangement), falling back to id as a
    // stable tie-break. The id tie-break is deliberate: it MUST match the engine's reorder, which renumbers
    // a sibling group by (order, then id) (see WorldRpc::move_entity). If the editor tie-broke differently
    // (e.g. by name) then siblings sharing order 0 — freshly created or legacy entities never explicitly
    // arranged — would display in one order here but be renumbered into another on the first move, so the
    // whole group would visibly reshuffle. Tying both sides to id keeps a move touching only the dragged row.
    private static void SortRecursively(List<EntityDescription> nodes)
    {
        nodes.Sort((a, b) =>
        {
            var byOrder = a.Order.CompareTo(b.Order);
            return byOrder != 0 ? byOrder : a.Id.CompareTo(b.Id);
        });
        foreach (var node in nodes)
            SortRecursively(node.Children);
    }

    private static IReadOnlyList<PropertyNode> ParsePropertiesCore(JObject json)
    {
        var nodes = new List<PropertyNode>(json.Count);
        foreach (var property in json.Properties())
        {
            // [[editor::hidden]] fields are dropped here, the single choke point feeding every grid
            // (inspector, app settings, editor settings), so they never become widgets.
            var node = Build(property.Name, Unwrap(property.Value));
            if (!node.Hidden)
                nodes.Add(node);
        }

        // Present fields in declaration (source) order, which the alphabetical JSON keys otherwise lose.
        // OrderBy is stable, so untyped data (no order attribute → all 0) keeps its original JSON order.
        return nodes.OrderBy(node => node.Order).ToList();
    }

    private static PropertyNode Build(string name, Wrapper wrapper)
    {
        var type = wrapper.Type;
        var value = wrapper.Value;

        if (value is JObject obj && IsComposite(type, obj))
            return Leaf(name, type is UnknownToken or "" ? ObjectToken : type, value, wrapper, ParsePropertiesCore(obj));

        if (value is JArray array && IsCompositeArray(type))
            return Leaf(name, ArrayToken, value, wrapper, ParseArrayElements(array));

        return Leaf(name, ResolveLeafType(wrapper), value, wrapper, children: null);
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
        if (wrapper.Type is "handle" or "entity")
            return wrapper.Type;

        if (wrapper.Choices is { Count: > 0 })
            return "enum";

        if (wrapper.Nested == "quat")
            return "quat";

        // Infer the widget from the JSON value for an untyped value (plain JSON), and also for a bare scalar
        // the engine could only tag structurally as "object" — an unsigned integer, or a choice-less enum
        // (one not marked [[serializable]], so it advertises no choices). Without this those settings would
        // fall through to the unknown-type placeholder instead of showing as a number field.
        var structural = wrapper.Type;
        if (structural == UnknownToken
            || (structural == ObjectToken && wrapper.Value is JValue { Type: not JTokenType.Null }))
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
        && (type is UnknownToken or "" or ObjectToken || value.Properties().Any(p => IsTypedWrapper(p.Value)));

    /// <summary>
    /// An array value is composite when it is plain JSON (no type tag) or the explicit "array" token (a
    /// std::vector). Math aggregates (vecN/matN/quat) are also JSON arrays but carry their own leaf tokens.
    /// </summary>
    private static bool IsCompositeArray(string type) => type is UnknownToken or "" or ArrayToken;

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
        if (obj.TryGetValue(AttributesKey, out var attributes)
            && attributes is JObject attributesObject
            && obj.TryGetValue(ValueKey, out var attributedValue))
        {
            metadata = attributesObject;
            value = attributedValue;
            return true;
        }

        if (obj.TryGetValue(TypeKey, out var typeToken)
            && typeToken.Type == JTokenType.String
            && obj.TryGetValue(ValueKey, out var leanValue))
        {
            metadata = obj;
            value = leanValue;
            return true;
        }

        metadata = obj;
        value = JValue.CreateNull();
        return false;
    }

    private static PropertyNode Leaf(
        string name,
        string type,
        JToken value,
        Wrapper wrapper,
        IReadOnlyList<PropertyNode>? children) =>
        new()
        {
            Name = name,
            Type = type,
            Value = value,
            Category = wrapper.Category,
            Description = wrapper.Description,
            View = wrapper.View,
            Label = wrapper.Label,
            ReadOnly = wrapper.ReadOnly,
            Hidden = wrapper.Hidden,
            Order = wrapper.Order,
            Icon = wrapper.Icon,
            IconColor = wrapper.IconColor,
            IsDefault = wrapper.IsDefault,
            ElementTemplate = wrapper.ElementTemplate,
            Choices = children is null ? wrapper.Choices : null,
            Children = children ?? [],
        };

    /// <summary>
    /// The single place the <c>{ "type", "value", metadata… }</c> convention is decoded — used both for the
    /// entity envelope (id/name/tag/parent) and the property tree. A "variant"-typed wrapper surfaces its
    /// active alternative. A bare value (plain JSON, or a missing token) returns type "unknown".
    /// </summary>
    private static Wrapper Unwrap(JToken? member)
    {
        if (member is JObject obj && TryReadWrapper(obj, out var metadata, out var value))
        {
            var type = metadata.Value<string>(TypeKey) ?? UnknownToken;

            // A variant property surfaces as its active alternative: its value is itself a typed wrapper
            // (lean or attributed), so adopt the alternative's type/value while keeping the field's editor
            // metadata (category/description/etc. live on the outer wrapper).
            if (type == VariantToken && value is JObject alternative)
            {
                var alt = Unwrap(alternative);
                if (alt.Type != UnknownToken)
                {
                    type = alt.Type;
                    value = alt.Value;
                }
            }

            return new Wrapper(
                type,
                metadata.Value<string>(NestedKey),
                value,
                metadata.Value<string>(CategoryKey),
                metadata.Value<string>(DescriptionKey),
                metadata.Value<string>(ViewKey),
                metadata.Value<string>(LabelKey),
                metadata.Value<bool?>(ReadOnlyKey) ?? false,
                metadata.Value<bool?>(HiddenKey) ?? false,
                metadata.Value<int?>(OrderKey) ?? 0,
                metadata.Value<string>(IconKey),
                metadata.Value<string>(IconColorKey),
                ReadChoices(metadata[ChoicesKey]),
                // is_default sits next to attributes/value on the node itself (describe-only), not inside
                // the attributes object.
                obj.Value<bool?>(IsDefaultKey) ?? false,
                metadata[ElementTemplateKey]);
        }

        return new Wrapper(
            UnknownToken, null, member ?? JValue.CreateNull(), null, null, null, null, false, false, 0,
            null, null, null, false, null);
    }

    private static IReadOnlyList<string>? ReadChoices(JToken? token) =>
        token is JArray array
            ? array.Select(element => element.Value<string>() ?? "").ToList()
            : null;

    private static string InferToken(JToken value) => value.Type switch
    {
        JTokenType.Integer => "int",
        JTokenType.Float => "float",
        JTokenType.Boolean => "bool",
        JTokenType.String => "string",
        JTokenType.Array => ArrayToken,
        JTokenType.Object => ObjectToken,
        _ => UnknownToken,
    };

    /// <summary>
    /// The pieces of a typed-JSON property wrapper, after splitting off the engine's metadata keys.
    /// </summary>
    private readonly record struct Wrapper(
        string Type,
        // The unwrapped semantic type name from the describe "nested" attribute (e.g. "quat" for a rotation
        // that shares the structural "vec4" token). Null on the lean/persisted path, which carries no
        // attributes. Lets the parser route such a value to the right widget — see ResolveLeafType.
        string? Nested,
        JToken Value,
        string? Category,
        string? Description,
        string? View,
        string? Label,
        bool ReadOnly,
        bool Hidden,
        int Order,
        string? Icon,
        string? IconColor,
        IReadOnlyList<string>? Choices,
        bool IsDefault,
        // The describe-only "element_template" for a resizable list: the JSON of one default element,
        // which the list widget clones to append. Null for everything else.
        JToken? ElementTemplate);
}
