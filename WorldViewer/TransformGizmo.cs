using Avalonia.Media;
using System.Numerics;
using Toybox.Studio.EngineApi.Types.Gizmos;

namespace Toybox.Studio.WorldViewer;

/// <summary>
/// The world viewport's transform-gizmo looks: builds each <see cref="GizmoMode"/>'s handle set with the
/// gizmo drawing API (<see cref="GizmoRenderer"/>), in gizmo units — the pivot at the origin, 1 = the
/// engine's constant-screen-size scale. Select composes every family into the combined "super" gizmo;
/// the dedicated modes carry just their own. Handle order is hit-test priority (first hit wins), so
/// the most precise targets — the centre cube and the axis knobs — come before the arrows and rings
/// they overlap.
/// </summary>
public static class TransformGizmo
{
    // The Godot editor's axis colors (X red, Y green, Z blue) and the neutral uniform-scale grey.
    private static readonly Color AxisX = Color.FromRgb(245, 51, 82);
    private static readonly Color AxisY = Color.FromRgb(135, 214, 3);
    private static readonly Color AxisZ = Color.FromRgb(41, 140, 245);
    private static readonly Color Uniform = Color.FromRgb(217, 217, 224);

    // The translucent fill alpha for the planar (two-axis) drag handles; their border draws opaque.
    private const byte PlaneFillAlpha = 64;

    private static readonly (GizmoAxis Axis, Vector3 Direction, Color Color)[] Axes =
    [
        (GizmoAxis.X, Vector3.UnitX, AxisX),
        (GizmoAxis.Y, Vector3.UnitY, AxisY),
        (GizmoAxis.Z, Vector3.UnitZ, AxisZ),
    ];

    // The three planar handles, keyed by the plane's normal axis. The two in-plane axes (A, B) match
    // the engine's axis_plane_basis order for that normal, so the drawn quad centre lines up exactly
    // with the analytic hit-test centre (pivot + (u+v)*extent). Colored by the normal axis, Godot-style.
    private static readonly (GizmoAxis Normal, Vector3 NormalDir, Vector3 A, Vector3 B, Color Color)[] Planes =
    [
        (GizmoAxis.X, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, AxisX),
        (GizmoAxis.Y, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX, AxisY),
        (GizmoAxis.Z, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, AxisZ),
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
                    .SolidCylinder(Vector3.Zero, direction * 0.85f, 0.011f * t)
                    .SolidCone(direction * 0.85f, direction, 0.05f * t)),
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

    // The dedicated translate gizmo: three Godot-style round arrows (thin cylinder shaft + cone head)
    // plus the three planar two-axis handles. Planes come first — they are the smaller, more specific
    // targets and sit near the arrow shafts (handle order is hit-test priority).
    private static List<GizmoHandle> BuildTranslate(float t)
    {
        var handles = new List<GizmoHandle>();
        handles.AddRange(PlaneHandles(GizmoHandleKind.Plane));
        foreach (var (axis, direction, color) in Axes)
            handles.Add(new GizmoHandle
            {
                Kind = GizmoHandleKind.Arrow,
                Axis = axis,
                Extent = 1.0,
                Ops = Draw(drawing => drawing
                    .Color(color)
                    .SolidCylinder(Vector3.Zero, direction * 0.85f, 0.012f * t)
                    .SolidCone(direction * 0.85f, direction, 0.06f * t)),
            });
        return handles;
    }

    // The dedicated rotate gizmo: three colored axis rings, plus the outer screen-space view ring
    // (billboarded to the camera and drawn engine-side, so it carries no ops). The axis rings come
    // first — they are the inner, more specific targets.
    private static List<GizmoHandle> BuildRotate(float t)
    {
        var handles = new List<GizmoHandle>();
        foreach (var (axis, direction, color) in Axes)
            handles.Add(new GizmoHandle
            {
                Kind = GizmoHandleKind.Ring,
                Axis = axis,
                Extent = 1.0,
                Ops = Draw(drawing => drawing
                    .Color(color)
                    .SolidTorus(Vector3.Zero, direction, 1.0f, 0.028f * t)),
            });
        handles.Add(new GizmoHandle { Kind = GizmoHandleKind.Ring, Axis = GizmoAxis.View, Extent = 1.15 });
        return handles;
    }

    // The dedicated scale gizmo: the uniform centre cube, the three planar two-axis handles, and the
    // three axis shafts with cube tips. Centre → planes → knobs is the hit-test priority order.
    private static List<GizmoHandle> BuildScale(float t)
    {
        var handles = new List<GizmoHandle> { Center(size: 0.13f * t) };
        handles.AddRange(PlaneHandles(GizmoHandleKind.PlaneScale));
        foreach (var (axis, direction, color) in Axes)
            handles.Add(new GizmoHandle
            {
                Kind = GizmoHandleKind.Knob,
                Axis = axis,
                Extent = 1.0,
                Ops = Draw(drawing => drawing
                    .Color(color)
                    .SolidCylinder(Vector3.Zero, direction, 0.02f * t)
                    .SolidBox(direction, new Vector3(0.09f * t))),
            });
        return handles;
    }

    // The planar drag handles (one per coordinate plane): a translucent axis-colored square offset into
    // the plane with an opaque border. <paramref name="kind"/> selects the drag semantics — Plane
    // (translate two axes) or PlaneScale (scale two axes). Extent is the corner offset the engine's
    // hit-test uses to place the quad centre, so it must match the drawn centre.
    private static IEnumerable<GizmoHandle> PlaneHandles(GizmoHandleKind kind)
    {
        const float offset = 0.30f;
        const float size = 0.18f;
        const float half = size * 0.5f;
        foreach (var (normal, normalDir, a, b, color) in Planes)
        {
            var center = (a + b) * offset;
            var fill = Color.FromArgb(PlaneFillAlpha, color.R, color.G, color.B);
            var c0 = center + (a * half) + (b * half);
            var c1 = center + (a * half) - (b * half);
            var c2 = center - (a * half) - (b * half);
            var c3 = center - (a * half) + (b * half);
            yield return new GizmoHandle
            {
                Kind = kind,
                Axis = normal,
                Extent = offset,
                Ops = Draw(drawing => drawing
                    .Color(fill)
                    .SolidPlane(center, normalDir, size)
                    .Color(color)
                    .Line(c0, c1)
                    .Line(c1, c2)
                    .Line(c2, c3)
                    .Line(c3, c0)),
            };
        }
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
