using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// The shared overlap behavior of the shape-specific triggers (<see cref="BoxTrigger"/>,
/// <see cref="SphereTrigger"/>, <see cref="CapsuleTrigger"/>, <see cref="MeshTrigger"/>), mirroring the
/// engine's <c>Trigger</c> — never attached to an entity directly, so abstract here. The engine's
/// overlap callbacks, occupant count, and manual-scan request are play-time runtime state and never
/// serialize, so only the persisted settings mirror.
/// </summary>
public abstract partial class Trigger : Component
{
    protected Trigger()
    {
        OverlapExecutionMode = ColliderOverlapExecutionMode.Auto;
        IsOverlapEnabled = true;
    }

    [EngineSync]
    public partial ColliderOverlapExecutionMode OverlapExecutionMode { get; set; }

    [EngineSync]
    public partial bool IsOverlapEnabled { get; set; }
}
