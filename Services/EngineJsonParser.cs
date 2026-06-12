using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services;

/// <summary>
/// Parses the engine's self-describing JSON into the editor's UI-ready models. It owns two related jobs:
/// turning a world.describe reply (entities + their components) into a <see cref="WorldNode"/> hierarchy,
/// and turning any typed-JSON object — every property written as
/// <c>{ "type": &lt;token&gt;, "value": &lt;value&gt; }</c> — into a <see cref="PropertyNode"/> tree the
/// property grid can render. The same property parser also handles plain JSON (settings files, reflected
/// POCOs), inferring tokens from the JSON shape; legacy bare values degrade to an "unknown" leaf.
///
/// This consolidates parsing that previously lived as static helpers (the old <c>PropertyTree</c>) and as
/// private tree-building methods on <see cref="WorldManager"/>, so there is one place that knows the
/// engine's wire conventions.
/// </summary>
public sealed class EngineJsonParser
{
    // Mirror the engine's PROPERTY_*_KEY (serialization.h). They share the variant convention's
    // "type"/"value" shape; a std::variant property is the one ambiguous case and is tagged with the
    // "variant" token, its value being the variant's own { type, value } alternative.
    private const string TypeKey = "type";
    private const string ValueKey = "value";
    private const string CategoryKey = "category";
    private const string DescriptionKey = "description";
    private const string ChoicesKey = "choices";
    private const string ViewKey = "view";
    private const string ReadOnlyKey = "readonly";
    private const string HiddenKey = "hidden";
    private const string VariantToken = "variant";

    // Tokens whose value is rendered by a dedicated leaf widget and must not be expanded into children,
    // even when the value is a JObject (e.g. a color carries r/g/b/a sub-fields).
    private static readonly HashSet<string> LeafTokens =
    [
        "int", "float", "double", "bool", "string", "uuid", "handle", "enum",
        "vec2", "vec3", "vec4", "quat", "color", "mat2", "mat3", "mat4",
    ];

    //// WORLD / ENTITY / COMPONENT ////

    /// <summary>
    /// Flattens a world description into a sorted scene hierarchy, parsing each entity's components into
    /// their property trees along the way. Pure and CPU-bound — callers run it off the UI thread.
    /// </summary>
    public IReadOnlyList<WorldNode> ParseWorld(WorldDescription description)
    {
        var nodes = new Dictionary<ulong, WorldNode>();
        foreach (var entity in description.Entities)
        {
            nodes[entity.Id] = new WorldNode
            {
                Id = entity.Id,
                Name = string.IsNullOrEmpty(entity.Name) ? $"Entity {entity.Id}" : entity.Name,
                Tag = entity.Tag ?? "",
                Components = ParseComponents(entity),
            };
        }

        var roots = new List<WorldNode>();
        foreach (var entity in description.Entities)
        {
            var node = nodes[entity.Id];
            if (entity.Parent != 0 && nodes.TryGetValue(entity.Parent, out var parent))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }

        SortRecursively(roots);
        return roots;
    }

    private List<WorldComponent> ParseComponents(WorldEntity entity)
    {
        var components = new List<WorldComponent>();
        if (entity.Components is null)
            return components;

        foreach (var property in entity.Components.Properties())
        {
            if (property.Value is not JObject raw)
                continue;

            components.Add(new WorldComponent(property.Name, raw, ParseProperties(raw)));
        }

        components.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return components;
    }

    private static void SortRecursively(List<WorldNode> nodes)
    {
        nodes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        foreach (var node in nodes)
            SortRecursively(node.Children);
    }

    //// TYPED-JSON PROPERTIES ////

    /// <summary>
    /// Parses an object into a property tree. Works for both the engine's typed JSON (each member is a
    /// <c>{ "type", "value" }</c> wrapper) and plain JSON (settings files / reflected POCOs), where
    /// tokens are inferred from the JSON shape. Composite values recurse so nested structs and objects
    /// render as sub-grids either way.
    /// </summary>
    public IReadOnlyList<PropertyNode> ParseProperties(JObject json)
    {
        var nodes = new List<PropertyNode>(json.Count);
        foreach (var property in json.Properties())
        {
            // [[editor::hidden]] fields ($hidden) are dropped here, the single choke point feeding
            // every grid (inspector, app settings, editor settings), so they never become widgets.
            var node = BuildMember(property.Name, property.Value);
            if (!node.Hidden)
                nodes.Add(node);
        }

        return nodes;
    }

    private PropertyNode BuildMember(string name, JToken member) => Build(name, Unwrap(member));

    private PropertyNode Build(string name, Wrapper wrapper)
    {
        var type = wrapper.Type;
        var value = wrapper.Value;

        // Dedicated leaf widgets (incl. color, which carries r/g/b/a sub-fields) never expand.
        if (!LeafTokens.Contains(type))
        {
            if (value is JObject obj)
                return Leaf(name, type == "unknown" ? "object" : type, value, wrapper, ParseProperties(obj));

            if (value is JArray array)
                return Leaf(name, "array", value, wrapper, BuildElements(array));
        }

        return Leaf(name, type == "unknown" ? InferToken(value) : type, value, wrapper, children: null);
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
            ReadOnly = wrapper.ReadOnly,
            Hidden = wrapper.Hidden,
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
    /// The pieces of a typed-JSON property wrapper, after splitting off the engine's <c>$</c>-prefixed
    /// metadata keys.
    /// </summary>
    private readonly record struct Wrapper(
        string Type,
        JToken Value,
        string? Category,
        string? Description,
        string? View,
        bool ReadOnly,
        bool Hidden,
        IReadOnlyList<string>? Choices);

    /// <summary>
    /// Splits a <c>{ "type", "value", "category"?, "description"?, "view"?, "readonly"?, "hidden"? }</c>
    /// wrapper. A wrapper is any object carrying a string "type" and "value" (extra metadata keys are
    /// fine). A "variant"-typed wrapper surfaces its inner alternative. Bare values return type "unknown".
    /// </summary>
    private static Wrapper Unwrap(JToken member)
    {
        if (member is JObject obj
            && obj.TryGetValue(TypeKey, out var typeToken)
            && typeToken.Type == JTokenType.String
            && obj.TryGetValue(ValueKey, out var valueToken))
        {
            var type = typeToken.Value<string>() ?? "unknown";
            var value = valueToken;

            // A variant property surfaces as its active alternative: its value is itself a
            // { type, value } wrapper, so adopt the alternative's type/value while keeping the field's
            // editor metadata (category/description/etc. live on the outer wrapper).
            if (type == VariantToken
                && value is JObject alternative
                && alternative.TryGetValue(TypeKey, out var altType)
                && altType.Type == JTokenType.String
                && alternative.TryGetValue(ValueKey, out var altValue))
            {
                type = altType.Value<string>() ?? "unknown";
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
                ReadChoices(obj[ChoicesKey]));
        }

        return new Wrapper("unknown", member, null, null, null, false, false, null);
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
        JTokenType.Array => "array",
        JTokenType.Object => "object",
        _ => "unknown",
    };
}
