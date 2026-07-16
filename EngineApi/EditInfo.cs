namespace Toybox.Studio.EngineApi;

/// <summary>
/// Describes one real, local synced edit — the payload of <see cref="EngineObject.Edited"/>.
/// <see cref="Key"/> is the edited property's wire key; an undo history uses it to coalesce a run of
/// edits to the same property (a gizmo drag, a slider scrub) into a single step.
/// </summary>
public readonly record struct EditInfo(string Key);
