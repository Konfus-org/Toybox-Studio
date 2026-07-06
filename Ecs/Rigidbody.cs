using System.Numerics;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>An entity's rigid body configuration, consumed by the runtime physics backends —
/// mirroring the engine's <c>Rigidbody</c>.</summary>
public sealed partial class Rigidbody : Component
{
    public Rigidbody()
    {
        Mass = 1.0f;
        IsGravityEnabled = true;
        TransformSyncMode = PhysicsTransformSyncMode.Sweep;
        Friction = 0.5f;
        LinearDamping = 0.05f;
        AngularDamping = 0.05f;
        IsSleepEnabled = true;
        SleepVelocityThreshold = 0.03f;
        SleepTimeSeconds = 0.5f;
    }

    [EngineSync]
    public partial float Mass { get; set; }

    [EngineSync]
    public partial bool IsKinematic { get; set; }

    [EngineSync]
    public partial bool IsGravityEnabled { get; set; }

    [EngineSync]
    public partial PhysicsTransformSyncMode TransformSyncMode { get; set; }

    [EngineSync]
    public partial Vector3 LinearVelocity { get; set; }

    [EngineSync]
    public partial Vector3 AngularVelocity { get; set; }

    [EngineSync]
    public partial float Friction { get; set; }

    [EngineSync]
    public partial float Restitution { get; set; }

    [EngineSync]
    public partial float LinearDamping { get; set; }

    [EngineSync]
    public partial float AngularDamping { get; set; }

    [EngineSync]
    public partial bool IsSleepEnabled { get; set; }

    [EngineSync]
    public partial float SleepVelocityThreshold { get; set; }

    [EngineSync]
    public partial float SleepTimeSeconds { get; set; }
}
