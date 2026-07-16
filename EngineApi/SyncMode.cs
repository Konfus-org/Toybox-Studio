namespace Toybox.Studio.EngineApi;

/// <summary>
/// How an engine-synced property flows between studio and engine — its binding direction, plus the
/// outbound timing for the two-way cases. <see cref="TwoWay"/> is the default.
/// </summary>
public enum SyncMode
{
    /// <summary>Studio ↔ engine: local edits push as they happen, inbound engine changes apply. The
    /// default, right for discrete edits (checkboxes, dropdowns).</summary>
    TwoWay,

    /// <summary>
    /// Two-way, but batched outbound: push the first change immediately, then coalesce rapid follow-ups
    /// into one trailing push per batch (tuned with the attribute's <c>batchFrequencyMs</c>). Right for
    /// continuous edits: sliders, gizmo drags, typing.
    /// </summary>
    Batched,

    /// <summary>Two-way, but staged outbound: changes are held locally until
    /// <see cref="EngineObject.CommitAsync"/> flushes them. Right for buffered editing (asset bodies
    /// saved as one unit).</summary>
    Manual,

    /// <summary>Engine → studio only. The generated property is read-only (get-only, or a private setter
    /// the class assigns its own engine-owned state through); local assignments never push. Right for
    /// state the editor observes but the engine owns — an asset's id, an entity's components.</summary>
    OneWayFromEngine,

    /// <summary>Studio → engine only. Local edits push, but inbound engine changes are ignored — the
    /// editor is the source of truth. Right for state the editor drives and the engine merely mirrors
    /// (the selection, gizmo overlays).</summary>
    OneWayFromStudio,
}
