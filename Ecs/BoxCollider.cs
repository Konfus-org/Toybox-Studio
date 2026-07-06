using System.Numerics;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>A solid axis-aligned box collision shape defined by half extents, mirroring the engine's
/// <c>BoxCollider</c>.</summary>
public sealed partial class BoxCollider : Component
{
    public BoxCollider() => HalfExtents = new Vector3(0.5f);

    public BoxCollider(Vector3 halfExtents) => HalfExtents = halfExtents;

    [EngineSync]
    public partial Vector3 HalfExtents { get; set; }
}
