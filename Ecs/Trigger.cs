using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Ecs;

/// <summary>
/// The shared overlap behavior of the shape-specific triggers (<see cref="BoxTrigger"/>,
/// <see cref="SphereTrigger"/>, <see cref="CapsuleTrigger"/>, <see cref="MeshTrigger"/>), mirroring the
/// engine's <c>Trigger</c> — never attached to an entity directly, so abstract here. The persisted
/// settings mirror as synced values; the engine's overlap callbacks surface as the synced events
/// (streamed only while handlers are attached, and only at play time — the editing world never
/// simulates), and the manual-scan request as <see cref="RequestOverlapScanAsync"/>.
/// </summary>
public abstract partial class Trigger : Component
{
    protected Trigger()
    {
        OverlapExecutionMode = ColliderOverlapExecutionMode.Auto;
        IsOverlapEnabled = true;
    }

    /// <summary>A body started overlapping this trigger. Raised on the UI thread.</summary>
    [EngineSync(Converter = typeof(OverlapConverter))]
    public partial event Action<Overlap> OverlapBegan;

    /// <summary>A body kept overlapping this trigger, once per physics tick. Raised on the UI thread;
    /// subscribe only where the per-tick stream is worth its wire traffic.</summary>
    [EngineSync(Converter = typeof(OverlapConverter))]
    public partial event Action<Overlap> OverlapStayed;

    /// <summary>A body stopped overlapping this trigger. Raised on the UI thread.</summary>
    [EngineSync(Converter = typeof(OverlapConverter))]
    public partial event Action<Overlap> OverlapEnded;

    [EngineSync]
    public partial ColliderOverlapExecutionMode OverlapExecutionMode { get; set; }

    [EngineSync]
    public partial bool IsOverlapEnabled { get; set; }

    /// <summary>Requests a manual overlap query on the next physics tick — the engine's
    /// <c>request_overlap_scan()</c>, for <see cref="ColliderOverlapExecutionMode.Manual"/> triggers.</summary>
    [EngineSync(EngineCommands.PhysicsOverlapScan)]
    public partial Task<Result> RequestOverlapScanAsync(CancellationToken ct = default);
}
