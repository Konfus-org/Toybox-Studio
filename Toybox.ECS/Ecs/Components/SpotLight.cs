using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Typed, engine-synced view of the <c>spot_light</c> component (a <see cref="Light"/> plus its range and
/// cone angles).</summary>
public sealed partial class SpotLight : Light
{
    [EngineSync] private float _range = 10.0f;

    [EngineSync] private float _innerAngle = 20.0f;

    [EngineSync] private float _outerAngle = 35.0f;
}
