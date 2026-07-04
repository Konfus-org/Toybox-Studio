namespace Toybox.Studio.EngineApi;

/// <summary>
/// When an engine-synced property's edits reach the engine. The mode governs only outbound timing —
/// inbound engine changes always apply immediately, whatever the mode.
/// </summary>
public enum SyncMode
{
    /// <summary>Push every change as it happens. Right for discrete edits: checkboxes, dropdowns.</summary>
    Live,

    /// <summary>
    /// Push the first change immediately, then coalesce rapid follow-ups into one trailing push per
    /// batch (tuned with the attribute's <c>batchFrequencyMs</c>). Right for continuous edits:
    /// sliders, gizmo drags, typing.
    /// </summary>
    Batched,

    /// <summary>Stage changes locally; nothing reaches the wire until <see cref="EngineObject.CommitAsync"/>.
    /// Right for buffered editing: heavy fields, asset bodies saved as one unit.</summary>
    Manual,

    /// <summary>Engine → studio only. The generated property is read-only; only inbound engine changes
    /// write it. Right for state the editor observes but never edits directly.</summary>
    Mirror,
}
