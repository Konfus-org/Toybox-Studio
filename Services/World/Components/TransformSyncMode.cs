namespace Toybox.Studio.Services.World.Components;

/// <summary>How runtime physics applies script-authored transform changes on a non-kinematic rigid body.
/// Mirrors the engine <c>PhysicsTransformSyncMode</c>.</summary>
public enum TransformSyncMode
{
    None = 0,
    Teleport = 1,
    Sweep = 2,
}
