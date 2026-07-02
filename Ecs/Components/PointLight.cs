using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Typed, engine-synced view of the <c>point_light</c> component (a <see cref="Light"/> plus its
/// effective range).</summary>
public sealed partial class PointLight : Light
{
    [EngineSync] private float _range = 10.0f;
}
