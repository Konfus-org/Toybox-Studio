using Newtonsoft.Json.Linq;
using Toybox.Studio.ECS;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.EngineApi;

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
    private const string TypeKey = "type";
    private const string ValueKey = "value";
    private const string CategoryKey = "category";
    private const string DescriptionKey = "description";
    private const string ChoicesKey = "choices";
    private const string ViewKey = "view";
    private const string ReadOnlyKey = "readonly";
    private const string HiddenKey = "hidden";
    private const string IconKey = "icon";
    private const string IconColorKey = "iconColor";
    private const string VariantToken = "variant";
    private const string ArrayToken = "array";
    private const string ObjectToken = "object";
    private const string UnknownToken = "unknown";

    /// <summary>
    /// Flattens a raw world.describe reply into a sorted entity hierarchy, parsing each entity's components
    /// into their property trees along the way. Pure and CPU-bound — callers run it off the UI thread.
    /// </summary>
    public IReadOnlyList<Entity> ParseWorld(JObject reply)
    {
        var componentTypes = reply["component_types"] as JObject;
        var entities = reply["entities"] as JArray ?? [];

        var nodes = new Dictionary<ulong, (Entity Node, ulong Parent)>();
        foreach (var token in entities)
        {
            if (token is not JObject entity)
                continue;

            var id = Unwrap(entity["id"]).Value.Value<ulong>();
            var name = Unwrap(entity["name"]).Value.Value<string>();
            var node = new Entity
            {
                Id = id,
                Name = string.IsNullOrEmpty(name) ? $"Entity {id}" : name,
                Tag = Unwrap(entity["tag"]).Value.Value<string>() ?? "",
                Components = ParseComponents(entity["components"] as JObject, componentTypes),
            };
            nodes[id] = (node, Unwrap(entity["parent"]).Value.Value<ulong>());
        }

        var roots = new List<Entity>();
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

    private List<Component> ParseComponents(JObject? components, JObject? componentTypes)
    {
        var result = new List<Component>();
        if (components is null)
            return result;

        foreach (var property in components.Properties())
        {
            if (property.Value is not JObject raw)
                continue;

            string? icon = null, iconColor = null;
            if (componentTypes?[property.Name] is JObject info)
                (icon, iconColor) = (info.Value<string>(IconKey), info.Value<string>(IconColorKey));

            result.Add(new Component(property.Name, raw, ParseProperties(raw), icon, iconColor));
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    private static void SortRecursively(List<Entity> nodes)
    {
        nodes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        foreach (var node in nodes)
            SortRecursively(node.Children);
    }

    /// <summary>
    /// Parses an object into a property tree. Works for both the engine's typed JSON (each member is a
    /// <c>{ "type", "value" }</c> wrapper) and plain JSON (settings files / reflected POCOs), where tokens
    /// are inferred from the JSON shape. Composite values recurse so nested structs/objects render as
    /// sub-grids either way.
    /// </summary>
    public IReadOnlyList<PropertyNode> ParseProperties(JObject json)
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

        return nodes;
    }

    private PropertyNode Build(string name, Wrapper wrapper)
    {
        var type = wrapper.Type;
        var value = wrapper.Value;

        if (value is JObject obj && IsComposite(type, obj))
            return Leaf(name, type is UnknownToken or "" ? ObjectToken : type, value, wrapper, ParseProperties(obj));

        if (value is JArray array && IsCompositeArray(type))
            return Leaf(name, ArrayToken, value, wrapper, BuildElements(array));

        return Leaf(name, type == UnknownToken ? InferToken(value) : type, value, wrapper, children: null);
    }

    /// <summary>
    /// An object value is composite (expands into a sub-grid) when it is plain JSON (no type tag), the
    /// explicit "object" token, or a struct — i.e. its members are themselves typed <c>{ type, value }</c>
    /// wrappers. Leaf aggregates (color/etc.) carry bare members and stay a single widget.
    /// </summary>
    private static bool IsComposite(string type, JObject value) =>
        type is UnknownToken or "" or ObjectToken || value.Properties().Any(p => IsTypedWrapper(p.Value));

    /// <summary>
    /// An array value is composite when it is plain JSON (no type tag) or the explicit "array" token (a
    /// std::vector). Math aggregates (vecN/matN/quat) are also JSON arrays but carry their own leaf tokens.
    /// </summary>
    private static bool IsCompositeArray(string type) => type is UnknownToken or "" or ArrayToken;

    private static bool IsTypedWrapper(JToken token) =>
        token is JObject obj
        && obj.TryGetValue(TypeKey, out var typeToken)
        && typeToken.Type == JTokenType.String
        && obj.ContainsKey(ValueKey);

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
            ReadOnly = wrapper.ReadOnly,
            Hidden = wrapper.Hidden,
            Icon = wrapper.Icon,
            IconColor = wrapper.IconColor,
            Choices = children is null ? wrapper.Choices : null,
            Children = children ?? [],
        };

    private IReadOnlyList<PropertyNode> BuildElements(JArray array)
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

    /// <summary>
    /// The pieces of a typed-JSON property wrapper, after splitting off the engine's metadata keys.
    /// </summary>
    private readonly record struct Wrapper(
        string Type,
        JToken Value,
        string? Category,
        string? Description,
        string? View,
        bool ReadOnly,
        bool Hidden,
        string? Icon,
        string? IconColor,
        IReadOnlyList<string>? Choices);

    /// <summary>
    /// The single place the <c>{ "type", "value", metadata… }</c> convention is decoded — used both for the
    /// entity envelope (id/name/tag/parent) and the property tree. A "variant"-typed wrapper surfaces its
    /// active alternative. A bare value (plain JSON, or a missing token) returns type "unknown".
    /// </summary>
    private static Wrapper Unwrap(JToken? member)
    {
        if (member is JObject obj
            && obj.TryGetValue(TypeKey, out var typeToken)
            && typeToken.Type == JTokenType.String
            && obj.TryGetValue(ValueKey, out var valueToken))
        {
            var type = typeToken.Value<string>() ?? UnknownToken;
            var value = valueToken;

            // A variant property surfaces as its active alternative: its value is itself a { type, value }
            // wrapper, so adopt the alternative's type/value while keeping the field's editor metadata
            // (category/description/etc. live on the outer wrapper).
            if (type == VariantToken
                && value is JObject alternative
                && alternative.TryGetValue(TypeKey, out var altType)
                && altType.Type == JTokenType.String
                && alternative.TryGetValue(ValueKey, out var altValue))
            {
                type = altType.Value<string>() ?? UnknownToken;
                value = altValue;
            }

            return new Wrapper(
                type,
                value,
                obj.Value<string>(CategoryKey),
                obj.Value<string>(DescriptionKey),
                obj.Value<string>(ViewKey),
                obj.Value<bool?>(ReadOnlyKey) ?? false,
                obj.Value<bool?>(HiddenKey) ?? false,
                obj.Value<string>(IconKey),
                obj.Value<string>(IconColorKey),
                ReadChoices(obj[ChoicesKey]));
        }

        return new Wrapper(
            UnknownToken, member ?? JValue.CreateNull(), null, null, null, false, false, null, null, null);
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
}
