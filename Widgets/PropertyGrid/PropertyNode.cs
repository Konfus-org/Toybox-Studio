using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

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
    /// Group heading from [[tbx::editor::category]], or null for the default group.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Tooltip text from [[tbx::editor::description]], or null.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// True when [[tbx::editor::readonly]] is set: the field is shown but not editable.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// True when [[tbx::editor::hidden]] is set: the field is dropped from the grid entirely.
    /// </summary>
    public bool Hidden { get; init; }

    /// <summary>
    /// Name of a custom editor control from [[tbx::editor::view]] / [View], or null to fall back to
    /// the type-driven widget. Resolved by the property-view registry.
    /// </summary>
    public string? View { get; init; }

    /// <summary>
    /// Editor icon for the value's type, from its [[tbx::icon]] attribute. Badges a nested struct/object
    /// header; null when the type is un-iconed.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// The icon's accent colour name (e.g. "BLUE") from [[tbx::icon]], or null.
    /// </summary>
    public string? IconColor { get; init; }

    public IReadOnlyList<PropertyNode> Children { get; init; } = [];

    public bool HasChildren => Children.Count > 0;
}
