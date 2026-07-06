using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>A solid sphere collision shape defined by radius, mirroring the engine's
/// <c>SphereCollider</c>.</summary>
public sealed partial class SphereCollider : Component
{
    public SphereCollider() => Radius = 0.5f;

    public SphereCollider(float radius) => Radius = radius;

    [EngineSync]
    public partial float Radius { get; set; }
}
