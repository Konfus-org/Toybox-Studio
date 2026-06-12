using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services;

/// <summary>
/// One node in a parsed property tree. Leaf nodes carry a <see cref="Value"/> token; composite nodes
/// (nested structs and arrays) carry <see cref="Children"/>. <see cref="Value"/> references the live
/// token inside the source JObject, so an in-place edit mutates the backing document.
/// </summary>
public sealed class PropertyNode
{
    public required string Name { get; init; }

    /// <summary>
    /// The engine type token (e.g. "float", "vec3", "color", "array") or "unknown".
    /// </summary>
    public required string Type { get; init; }

    public JToken? Value { get; init; }

    /// <summary>
    /// For an "enum" leaf, the selectable string values (from <c>$choices</c>); null otherwise. Drives
    /// the dropdown widget — without choices an enum falls back to a plain numeric editor.
    /// </summary>
    public IReadOnlyList<string>? Choices { get; init; }

    /// <summary>
    /// Group heading from [[tbx::category]], or null for the default group.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Tooltip text from [[tbx::description]], or null.
    /// </summary>
    public string? Description { get; init; }

    public IReadOnlyList<PropertyNode> Children { get; init; } = [];

    public bool HasChildren => Children.Count > 0;
}
