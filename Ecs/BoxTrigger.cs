using System.Numerics;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>A box-shaped overlap trigger defined by half extents, mirroring the engine's
/// <c>BoxTrigger</c>.</summary>
public sealed partial class BoxTrigger : Trigger
{
    public BoxTrigger() => HalfExtents = new Vector3(0.5f);

    public BoxTrigger(Vector3 halfExtents) => HalfExtents = halfExtents;

    [EngineSync]
    public partial Vector3 HalfExtents { get; set; }
}
