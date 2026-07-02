using Toybox.Studio.EngineApi;
using Toybox.Studio.Viewport;
using Toybox.Studio.Project;
using Toybox.Studio.Project.Assets;
using Toybox.Studio.Utils;
using Toybox.Studio.PropertyGrid;

namespace Toybox.Studio.Ecs.Components;

/// <summary>The projection an engine sky material is shown on. Mirrors the engine <c>SkyType</c>.</summary>
public enum SkyType
{
    Box = 0,
    Sphere = 1,
}

/// <summary>
/// Typed, engine-synced view of the <c>sky</c> component: the environment <see cref="Material"/> (a base-aware
/// material instance) and the projection <see cref="Type"/> it's shown on. Mirrors the engine's serialized
/// <c>Sky</c> fields.
/// </summary>
[Icon(Icon.CloudSun, PaletteColor.Cyan)]
[ViewportIcon(Icon.CloudSun, PaletteColor.Cyan)]
public sealed partial class Sky : Component
{
    [ViewModel(typeof(MaterialInstancePropertyViewModel))]
    [EngineSync] private MaterialInstance _material = new();

    [EngineSync] private SkyType _type = SkyType.Sphere;
}
