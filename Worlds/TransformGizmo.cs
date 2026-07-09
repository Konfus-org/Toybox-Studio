using System.Numerics;
using Avalonia.Media;
using Toybox.Studio.Gizmos;

namespace Toybox.Studio.Worlds;

/// <summary>
/// The editor's transform-gizmo looks: builds each <see cref="GizmoMode"/>'s handle set with the
/// gizmo drawing API (<see cref="GizmoRenderer"/>), in gizmo units — the pivot at the origin, 1 = the
/// engine's constant-screen-size scale. Select composes every family into the combined "super" gizmo;
/// the dedicated modes carry just their own. Handle order is hit-test priority (first hit wins), so
/// the most precise targets — the centre cube and the axis knobs — come before the arrows and rings
/// they overlap.
/// </summary>
public static class TransformGizmo
{
    // The classic axis colors (X red, Y green, Z blue) and the neutral uniform-scale grey.
    private static readonly Color AxisX = Color.FromRgb(235, 66, 66);
    private static readonly Color AxisY = Color.FromRgb(89, 217, 89);
    private static readonly Color AxisZ = Color.FromRgb(77, 128, 245);
    private static readonly Color Uniform = Color.FromRgb(217, 217, 224);

    private static readonly (GizmoAxis Axis, Vector3 Direction, Color Color)[] Axes =
    [
        (GizmoAxis.X, Vector3.UnitX, AxisX),
        (GizmoAxis.Y, Vector3.UnitY, AxisY),
        (GizmoAxis.Z, Vector3.UnitZ, AxisZ),
    ];

    /// <summary>
    /// The mode's handle set, in hit-test priority order. <paramref name="thickness"/> is the
    /// accessibility multiplier scaling how thick every handle draws (shafts, ring tubes, knob and
    /// centre cubes); reach (extents) and grab distances are unaffected.
    /// </summary>
    public static IReadOnlyList<GizmoHandle> HandlesFor(GizmoMode mode, double thickness = 1.0)
    {
        // Guard the multiplier: a mistyped settings value must never draw an invisible or
        // world-swallowing gizmo.
        var t = (float)Math.Clamp(thickness, 0.25, 3.0);
        return mode switch
        {
            GizmoMode.Select => BuildSelect(t),
            GizmoMode.Translate => BuildTranslate(t),
            GizmoMode.Rotate => BuildRotate(t),
            GizmoMode.Scale => BuildScale(t),
            _ => [],
        };
    }

    // The super gizmo: uniform-scale cube at the pivot, per-axis scale knobs on the inner shafts,
    // full translate arrows, and rotate rings tucked inside the arrow tips so ring/arrow overlap is
    // limited to two crossing points per ring. Deliberately the slimmest set — three families share
    // the same screen space, and slender geometry is what keeps each readable and grabbable.
    private static List<GizmoHandle> BuildSelect(float t)
    {
        const double rings = 0.62;
        const double knobs = 0.35;

        var handles = new List<GizmoHandle> { Center(size: 0.11f * t) };
        foreach (var (axis, direction, color) in Axes)
            handles.Add(new GizmoHandle
            {
                Kind = GizmoHandleKind.Knob,
                Axis = axis,
                Extent = knobs,
                Ops = Draw(drawing => drawing
                    .Color(color)
                    .SolidBox(direction * (float)knobs, new Vector3(0.065f * t))),
            });
        foreach (var (axis, direction, color) in Axes)
            handles.Add(new GizmoHandle
            {
                Kind = GizmoHandleKind.Arrow,
                Axis = axis,
                Extent = 1.0,
                Ops = Draw(drawing => drawing
                    .Color(color)
                    .SolidArrow(Vector3.Zero, direction, 0.016f * t)),
            });
        foreach (var (axis, direction, color) in Axes)
            handles.Add(new GizmoHandle
            {
                Kind = GizmoHandleKind.Ring,
                Axis = axis,
                Extent = rings,
                Ops = Draw(drawing => drawing
                    .Color(color)
                    .SolidTorus(Vector3.Zero, direction, (float)rings, 0.018f * t)),
            });
        return handles;
    }

    private static List<GizmoHandle> BuildTranslate(float t) =>
        [.. Axes.Select(axis => new GizmoHandle
        {
            Kind = GizmoHandleKind.Arrow,
            Axis = axis.Axis,
            Extent = 1.0,
            Ops = Draw(drawing => drawing
                .Color(axis.Color)
                .SolidArrow(Vector3.Zero, axis.Direction, 0.02f * t)),
        })];

    private static List<GizmoHandle> BuildRotate(float t) =>
        [.. Axes.Select(axis => new GizmoHandle
        {
            Kind = GizmoHandleKind.Ring,
            Axis = axis.Axis,
            Extent = 1.0,
            Ops = Draw(drawing => drawing
                .Color(axis.Color)
                .SolidTorus(Vector3.Zero, axis.Direction, 1.0f, 0.028f * t)),
        })];

    // The dedicated scale gizmo: full shafts with end knobs, plus the uniform centre cube.
    private static List<GizmoHandle> BuildScale(float t)
    {
        var handles = new List<GizmoHandle> { Center(size: 0.13f * t) };
        foreach (var (axis, direction, color) in Axes)
            handles.Add(new GizmoHandle
            {
                Kind = GizmoHandleKind.Knob,
                Axis = axis,
                Extent = 1.0,
                Ops = Draw(drawing => drawing
                    .Color(color)
                    .SolidBeam(Vector3.Zero, direction, 0.02f * t)
                    .SolidBox(direction, new Vector3(0.09f * t))),
            });
        return handles;
    }

    private static GizmoHandle Center(float size) => new()
    {
        Kind = GizmoHandleKind.Center,
        Axis = GizmoAxis.All,
        Extent = 0.0,
        Ops = Draw(drawing => drawing
            .Color(Uniform)
            .SolidBox(Vector3.Zero, new Vector3(size))),
    };

    private static IReadOnlyList<GizmoOp> Draw(Action<GizmoRenderer> draw)
    {
        var drawing = new GizmoRenderer();
        draw(drawing);
        return drawing.Build();
    }
}
