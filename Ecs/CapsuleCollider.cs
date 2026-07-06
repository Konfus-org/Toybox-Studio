using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>A solid capsule collision shape defined by radius and cylinder half height, mirroring the
/// engine's <c>CapsuleCollider</c>.</summary>
public sealed partial class CapsuleCollider : Component
{
    public CapsuleCollider()
    {
        Radius = 0.5f;
        HalfHeight = 0.5f;
    }

    public CapsuleCollider(float radius, float halfHeight)
    {
        Radius = radius;
        HalfHeight = halfHeight;
    }

    [EngineSync]
    public partial float Radius { get; set; }

    [EngineSync]
    public partial float HalfHeight { get; set; }
}
