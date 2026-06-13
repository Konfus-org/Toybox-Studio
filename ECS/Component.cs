using Newtonsoft.Json.Linq;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.ECS;

/// <summary>
/// One component on an entity. <see cref="Raw"/> is the original typed component JObject (kept so an edit
/// can read a single property's value back out); <see cref="Properties"/> is its parsed type-driven property
/// tree. <see cref="Icon"/>/<see cref="IconColor"/> come from the component type's [[tbx::icon]] and badge
/// the header (null when un-iconed).
/// </summary>
public sealed record Component(
    string Name,
    JObject Raw,
    IReadOnlyList<PropertyNode> Properties,
    string? Icon = null,
    string? IconColor = null);
