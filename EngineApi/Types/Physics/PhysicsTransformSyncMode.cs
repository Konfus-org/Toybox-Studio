using Toybox.Studio.EngineApi.Types.Components;
using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Physics;

/// <summary>How runtime physics applies script-authored transform changes on non-kinematic rigid
/// bodies, mirroring the engine's <c>PhysicsTransformSyncMode</c>.</summary>
public enum PhysicsTransformSyncMode
{
    None = 0,
    Teleport = 1,
    Sweep = 2,
}
