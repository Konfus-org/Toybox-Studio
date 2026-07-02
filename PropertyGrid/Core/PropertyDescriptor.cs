using Avalonia.Media;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The pure metadata half of a grid property — everything a widget needs to present a value EXCEPT the value
/// itself, which is owned by a paired <see cref="IValueAccessor"/>. This is the reflection-native successor to the
/// old <c>PropertyNode</c>: where that fused metadata with a live <c>JToken</c> parsed out of the engine's
/// describe JSON, a descriptor carries only the shape (type token, category, order, choices, …) and is produced
/// either by walking a typed C# model (<see cref="ReflectionWalk"/>) or, for engine-authored data, by reading a
/// describe body (<see cref="JsonDescriptorReader"/>). The same descriptor type serves both paths; the value
/// source differs behind the accessor.
/// </summary>
public sealed class PropertyDescriptor
{
    /// <summary>The property's raw (un-humanized) key — the wire name for a modelled field, or the JSON key.</summary>
    public required string Name { get; init; }

    /// <summary>The structural <see cref="EngineTypes"/> token that routes the value to its widget.</summary>
    public required string Type { get; init; }

    /// <summary>Enum options, or a reference type's asset-type filter; null for a plain value.</summary>
    public IReadOnlyList<string>? Choices { get; init; }

    /// <summary>Group heading (<c>[Category]</c>), or null for the default group.</summary>
    public string? Category { get; init; }

    /// <summary>Tooltip text (<c>[Description]</c>), or null.</summary>
    public string? Description { get; init; }

    /// <summary>Custom editor view-model type name (<c>[ViewModel]</c> / <c>[[tbx::view]]</c>), or null.</summary>
    public string? View { get; init; }

    /// <summary>Display-name override (<c>[DisplayName]</c>), or null to humanize <see cref="Name"/>.</summary>
    public string? Label { get; init; }

    /// <summary>True when the field is shown but not editable (<c>[ReadOnly]</c>).</summary>
    public bool ReadOnly { get; init; }

    /// <summary>True when the field is dropped from the grid entirely (<c>[Hidden]</c>).</summary>
    public bool Hidden { get; init; }

    /// <summary>Declaration index for source-order sorting (<c>[Order]</c>); defaults to 0.</summary>
    public int Order { get; init; }

    /// <summary>Editor icon for the value's type (<c>[Icon]</c>); <see cref="Icon.None"/> when un-iconed.</summary>
    public Icon Icon { get; init; }

    /// <summary>The icon's accent colour, or null.</summary>
    public Color? IconColor { get; init; }

    /// <summary>
    /// True when the value currently equals its default (engine-authoritative). Seeds the "modified" indicator;
    /// defaults to false where the default is unknown (lean settings), so a row never shows a false marker.
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// For a resizable list, the descriptor of one element — its shape drives an element row's editor. The
    /// backing accessor owns element CREATION (a typed default vs a cloned JSON template); this only describes
    /// the element. Null for fixed lists and non-list nodes; its presence marks an "array" node as resizable.
    /// </summary>
    public PropertyDescriptor? ElementTemplate { get; init; }

    /// <summary>Child descriptors for a composite (struct/list); empty for a leaf.</summary>
    public IReadOnlyList<PropertyDescriptor> Children { get; init; } = [];

    public bool HasChildren => Children.Count > 0;
}
