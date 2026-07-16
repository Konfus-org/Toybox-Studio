using Toybox.Studio.EngineApi.Types.Components;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Physics;

/// <summary>A sphere-shaped overlap trigger defined by radius, mirroring the engine's
/// <c>SphereTrigger</c>.</summary>
public sealed partial class SphereTrigger : Trigger
{
    public SphereTrigger() => Radius = 0.5f;

    public SphereTrigger(float radius) => Radius = radius;

    [EngineSync]
    public partial float Radius { get; set; }
}
