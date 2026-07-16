using Toybox.Studio.EngineApi.Types.Components;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Physics;

/// <summary>A solid sphere collision shape defined by radius, mirroring the engine's
/// <c>SphereCollider</c>.</summary>
public sealed partial class SphereCollider : Collider
{
    public SphereCollider() => Radius = 0.5f;

    public SphereCollider(float radius) => Radius = radius;

    [EngineSync]
    public partial float Radius { get; set; }
}
