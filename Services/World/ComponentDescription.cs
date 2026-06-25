using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Services.World;

/// <summary>
/// One component on an entity. <see cref="Raw"/> is the original typed component JObject (kept so an edit
/// can read a single property's value back out); <see cref="Properties"/> is its parsed type-driven property
/// tree. <see cref="Icon"/>/<see cref="IconColor"/> come from the component type's [[tbx::icon]] and badge
/// the header (null when un-iconed). <see cref="ViewportIcon"/>/<see cref="ViewportIconColor"/> come from the
/// type's [[tbx::viewport_icon]] and, when present, mark the component for a billboard icon in the 3D viewport.
/// The behavioral counterpart (set/get/reset against the engine) is the <see cref="Component"/> handle.
/// </summary>
public sealed record ComponentDescription(
    string Name,
    JObject Raw,
    IReadOnlyList<PropertyNode> Properties,
    string? Icon = null,
    string? IconColor = null,
    string? ViewportIcon = null,
    string? ViewportIconColor = null);
