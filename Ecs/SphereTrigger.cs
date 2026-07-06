using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>A sphere-shaped overlap trigger defined by radius, mirroring the engine's
/// <c>SphereTrigger</c>.</summary>
public sealed partial class SphereTrigger : Trigger
{
    public SphereTrigger() => Radius = 0.5f;

    public SphereTrigger(float radius) => Radius = radius;

    [EngineSync]
    public partial float Radius { get; set; }
}
