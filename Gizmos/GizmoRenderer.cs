using System.Numerics;
using Avalonia.Media;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Gizmos;

/// <summary>
/// A fluent builder for one gizmo layer's drawing, mirroring the engine's immediate-mode
/// <c>tbx::Gizmos</c> surface: state ops (<see cref="Color"/>, <see cref="Matrix"/>) apply to the
/// shapes that follow, exactly like <c>set_color</c>/<c>set_matrix</c> do engine-side. Geometry is
/// world space. <see cref="Build"/> snapshots the ops as the immutable list a
/// <see cref="GizmoLayer.Ops"/> assignment pushes; the builder itself is single-use scratch.
/// </summary>
public sealed class GizmoRenderer
{
    private readonly List<GizmoOp> _ops = [];

    /// <summary>The draw color applied to every shape that follows.</summary>
    public GizmoRenderer Color(Color color) => Add("color", WireValue.Write(color));

    /// <summary>A transform applied to every shape coordinate that follows, until
    /// <see cref="ResetMatrix"/>. Sent row-major (M11…M44); the engine transposes into its
    /// column-major matrices.</summary>
    public GizmoRenderer Matrix(Matrix4x4 matrix) => Add(
        "matrix",
        new JArray(
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44));

    public GizmoRenderer ResetMatrix() => Add("reset_matrix");

    public GizmoRenderer Line(Vector3 start, Vector3 end) =>
        Add("line", WireValue.Write(start), WireValue.Write(end));

    public GizmoRenderer Ray(Vector3 origin, Vector3 direction, float length) =>
        Add("ray", WireValue.Write(origin), WireValue.Write(direction), WireValue.Write(length));

    public GizmoRenderer WireBox(Vector3 center, Vector3 size, Quaternion? rotation = null) =>
        AddRotatable("wire_box", rotation, WireValue.Write(center), WireValue.Write(size));

    public GizmoRenderer WireSphere(Vector3 center, float radius) =>
        Add("wire_sphere", WireValue.Write(center), WireValue.Write(radius));

    /// <summary>A wireframe capsule: a cylinder of <paramref name="halfHeight"/> (half the straight
    /// section) capped by two hemispheres of <paramref name="radius"/>, aligned to the rotated local
    /// Y axis — the engine's capsule collider shape.</summary>
    public GizmoRenderer WireCapsule(Vector3 center, float radius, float halfHeight, Quaternion? rotation = null) =>
        AddRotatable(
            "wire_capsule", rotation, WireValue.Write(center), WireValue.Write(radius), WireValue.Write(halfHeight));

    /// <summary>A wire circle of <paramref name="radius"/> in the plane perpendicular to
    /// <paramref name="axis"/>.</summary>
    public GizmoRenderer Ring(Vector3 center, Vector3 axis, float radius) =>
        Add("ring", WireValue.Write(center), WireValue.Write(axis), WireValue.Write(radius));

    /// <summary>A wire arrow (line plus head) from point to point.</summary>
    public GizmoRenderer Arrow(Vector3 from, Vector3 to) =>
        Add("arrow", WireValue.Write(from), WireValue.Write(to));

    /// <summary>A solid arrow with body thickness: a square-section shaft of
    /// <paramref name="shaftRadius"/> plus a pyramid head.</summary>
    public GizmoRenderer SolidArrow(Vector3 from, Vector3 to, float shaftRadius) =>
        Add("solid_arrow", WireValue.Write(from), WireValue.Write(to), WireValue.Write(shaftRadius));

    /// <summary>A solid square-section beam (no head) of <paramref name="radius"/> between two points.</summary>
    public GizmoRenderer SolidBeam(Vector3 from, Vector3 to, float radius) =>
        Add("solid_beam", WireValue.Write(from), WireValue.Write(to), WireValue.Write(radius));

    /// <summary>A solid torus (thick ring) of major radius <paramref name="ringRadius"/> and tube
    /// radius <paramref name="tubeRadius"/>, in the plane perpendicular to <paramref name="axis"/>.</summary>
    public GizmoRenderer SolidTorus(Vector3 center, Vector3 axis, float ringRadius, float tubeRadius) =>
        Add(
            "solid_torus",
            WireValue.Write(center),
            WireValue.Write(axis),
            WireValue.Write(ringRadius),
            WireValue.Write(tubeRadius));

    /// <summary>A filled circular sector (pie) in the plane perpendicular to <paramref name="axis"/>,
    /// swept from <paramref name="startDirection"/> by <paramref name="sweepRadians"/> (signed).</summary>
    public GizmoRenderer FilledArc(
        Vector3 center, Vector3 axis, float radius, Vector3 startDirection, float sweepRadians) =>
        Add(
            "filled_arc",
            WireValue.Write(center),
            WireValue.Write(axis),
            WireValue.Write(radius),
            WireValue.Write(startDirection),
            WireValue.Write(sweepRadians));

    public GizmoRenderer WireSquare(Vector3 center, Vector2 size, Quaternion? rotation = null) =>
        AddRotatable("wire_square", rotation, WireValue.Write(center), WireValue.Write(size));

    public GizmoRenderer WirePlane(Vector3 center, Vector3 normal, float size) =>
        Add("wire_plane", WireValue.Write(center), WireValue.Write(normal), WireValue.Write(size));

    /// <summary>Three colored axes (X red, Y green, Z blue) — the "transform" gizmo look.</summary>
    public GizmoRenderer Axes(Vector3 origin, Quaternion rotation, float size) =>
        Add("axes", WireValue.Write(origin), WireValue.Write(rotation), WireValue.Write(size));

    public GizmoRenderer SolidBox(Vector3 center, Vector3 size, Quaternion? rotation = null) =>
        AddRotatable("solid_box", rotation, WireValue.Write(center), WireValue.Write(size));

    public GizmoRenderer SolidSphere(Vector3 center, float radius) =>
        Add("solid_sphere", WireValue.Write(center), WireValue.Write(radius));

    public GizmoRenderer SolidSquare(Vector3 center, Vector2 size, Quaternion? rotation = null) =>
        AddRotatable("solid_square", rotation, WireValue.Write(center), WireValue.Write(size));

    public GizmoRenderer SolidPlane(Vector3 center, Vector3 normal, float size) =>
        Add("solid_plane", WireValue.Write(center), WireValue.Write(normal), WireValue.Write(size));

    /// <summary>The accumulated ops as the immutable list a <see cref="GizmoLayer.Ops"/> assignment
    /// pushes. Always a fresh list, so reassigning a rebuilt drawing registers as a change.</summary>
    public IReadOnlyList<GizmoOp> Build() => [.. _ops];

    private GizmoRenderer Add(string op, params JToken[] args)
    {
        var token = new JArray(op);
        foreach (var arg in args)
            token.Add(arg);
        _ops.Add(new GizmoOp(token));
        return this;
    }

    // A shape with the engine's optional trailing rotation parameter: omitted from the wire when unset,
    // so the engine default (identity) applies.
    private GizmoRenderer AddRotatable(string op, Quaternion? rotation, params JToken[] args) =>
        rotation is { } value ? Add(op, [.. args, WireValue.Write(value)]) : Add(op, args);
}
