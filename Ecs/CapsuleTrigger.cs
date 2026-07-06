using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>A capsule-shaped overlap trigger defined by radius and cylinder half height, mirroring the
/// engine's <c>CapsuleTrigger</c>.</summary>
public sealed partial class CapsuleTrigger : Trigger
{
    public CapsuleTrigger()
    {
        Radius = 0.5f;
        HalfHeight = 0.5f;
    }

    public CapsuleTrigger(float radius, float halfHeight)
    {
        Radius = radius;
        HalfHeight = halfHeight;
    }

    [EngineSync]
    public partial float Radius { get; set; }

    [EngineSync]
    public partial float HalfHeight { get; set; }
}
