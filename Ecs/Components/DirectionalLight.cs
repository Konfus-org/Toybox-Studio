using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;
using Toybox.Studio.Viewport;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Typed, engine-synced view of the <c>directional_light</c> component (a <see cref="Light"/> plus its
/// ambient contribution).</summary>
[ViewportIcon(Icon.Sun, PaletteColor.Yellow)]
public sealed partial class DirectionalLight : Light
{
    [EngineSync] private float _ambient = 0.03f;
}
