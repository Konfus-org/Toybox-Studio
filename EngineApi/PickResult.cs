namespace Toybox.Studio.EngineApi;

/// <summary>
/// The engine's <c>view.pick</c> reply: the entity under the picked point (<see cref="Id"/>, null on
/// a miss), or <see cref="Gizmo"/> when the cursor was on a transform-gizmo handle instead — gizmo
/// intent, so the caller neither selects nor clears.
/// </summary>
public sealed record PickResult(ulong? Id, bool Gizmo);
