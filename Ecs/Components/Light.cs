using Avalonia.Media;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Viewport;
using Toybox.Studio.Project;

namespace Toybox.Studio.Ecs.Components;

/// <summary>
/// Shared base of the realtime light components, mirroring the engine's <c>Light : Component</c> — the colour,
/// intensity and shadow-casting fields every light type carries. Not a component on its own (abstract, so the
/// reflect catalog skips it); the concrete lights (<see cref="DirectionalLight"/>, <see cref="PointLight"/>,
/// <see cref="SpotLight"/>, <see cref="AreaLight"/>) derive it and add their own fields. The generator chains the
/// reflected-field machinery through the base, so a derived light's grid carries these fields too.
/// </summary>
[IconAttribute(Icon.Lightbulb, Toybox.Studio.Utils.PaletteColor.Yellow)]
[ViewportIconAttribute(Icon.Lightbulb, Toybox.Studio.Utils.PaletteColor.Yellow)]
public abstract partial class Light : Component
{
    [EngineSync] private Color _color = Colors.White;

    [EngineSync] private float _intensity = 1.0f;

    [EngineSync] private bool _castShadows = true;
}
