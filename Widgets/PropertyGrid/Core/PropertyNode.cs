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
    /// Group heading from [[tbx::category]], or null for the default group.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Tooltip text from [[tbx::description]], or null.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// True when [[tbx::readonly]] is set: the field is shown but not editable.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// True when [[tbx::hidden]] is set: the field is dropped from the grid entirely.
    /// </summary>
    public bool Hidden { get; init; }

    /// <summary>
    /// The field's declaration index within its struct, from the describe response's <c>order</c> attribute.
    /// The grid presents properties sorted by this (source order) rather than the alphabetical key order the
    /// engine's JSON map imposes. Defaults to 0 for untyped data (settings files), preserving JSON order.
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// Name of a custom editor control from [[tbx::view]] / [View], or null to fall back to
    /// the type-driven widget. Resolved by the property-view registry.
    /// </summary>
    public string? View { get; init; }

    /// <summary>
    /// Display name from [[tbx::label]], overriding the humanized field key without changing the
    /// serialized key (e.g. a "material" handle shown as "Base"). Null falls back to the humanized name.
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// Editor icon for the value's type, from its [[tbx::icon]] attribute. Badges a nested struct/object
    /// header; null when the type is un-iconed.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// The icon's accent colour name (e.g. "BLUE") from [[tbx::icon]], or null.
    /// </summary>
    public string? IconColor { get; init; }

    /// <summary>
    /// True when the property currently holds its default value, from the describe response's
    /// <c>is_default</c> flag. Lets the grid show a "modified" indicator and offer a reset. Defaults to
    /// false for data without the flag (e.g. lean settings files).
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// For a resizable list (a <c>std::vector</c>), the JSON of one default-constructed element, from the
    /// describe response's <c>element_template</c> attribute. The list widget clones it to append a new
    /// entry — so an empty vector can still be grown. Null for fixed lists and non-list nodes; its presence
    /// is what marks an "array" node as resizable.
    /// </summary>
    public JToken? ElementTemplate { get; init; }

    public IReadOnlyList<PropertyNode> Children { get; init; } = [];

    public bool HasChildren => Children.Count > 0;
}
