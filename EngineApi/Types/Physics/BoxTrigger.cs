using System.Numerics;
using Toybox.Studio.EngineApi.Types.Components;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Physics;

/// <summary>A box-shaped overlap trigger defined by half extents, mirroring the engine's
/// <c>BoxTrigger</c>.</summary>
public sealed partial class BoxTrigger : Trigger
{
    public BoxTrigger() => HalfExtents = new Vector3(0.5f);

    public BoxTrigger(Vector3 halfExtents) => HalfExtents = halfExtents;

    [EngineSync]
    public partial Vector3 HalfExtents { get; set; }
}
