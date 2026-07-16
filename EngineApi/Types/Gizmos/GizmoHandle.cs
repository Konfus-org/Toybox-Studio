using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Gizmos;

/// <summary>
/// One editor-authored transform handle, as <c>view.setGizmo</c> pushes it: the kind + axis give the
/// engine its drag semantics and analytic hit shape, <see cref="Extent"/> is its reach in gizmo units
/// (a unit space anchored at the selection pivot, scaled engine-side to a constant screen size), and
/// <see cref="Ops"/> is its look — a unit-space <see cref="GizmoRenderer"/> drawing the engine replays
/// under the pivot/size frame, tinting it on hover/drag.
/// </summary>
public sealed record GizmoHandle
{
    public required GizmoHandleKind Kind { get; init; }

    public GizmoAxis Axis { get; init; } = GizmoAxis.All;

    public double Extent { get; init; } = 1.0;

    public IReadOnlyList<GizmoOp> Ops { get; init; } = [];
}
