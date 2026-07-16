using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Gizmos;

/// <summary>
/// One retained, named layer of the editor's gizmo overlay, mirrored to the engine: the engine keeps
/// the layer's drawing resident and renders it over editor viewports until it is replaced or removed,
/// so a static overlay costs the wire nothing after its one push. Class-level Batched sync is the drag
/// story — a layer redrawn every pointer-move pushes its leading edge at once and coalesces the rest,
/// so live tool feedback feels immediate without flooding the wire. Layers are created and owned by
/// <see cref="Gizmo"/>; call sites draw through it.
/// </summary>
[EngineSync(EngineCommands.GizmoSet, SyncMode.Batched, Address = "gizmos/{Name}")]
public sealed partial class GizmoLayer
{
    public GizmoLayer(string name)
    {
        Name = name;
        Ops = [];
        View = string.Empty;
        IsVisible = true;
    }

    /// <summary>The layer's identity — part of the wire address, fixed for the layer's lifetime.</summary>
    public string Name { get; }

    /// <summary>The layer's drawing, replayed in order by the engine's gizmo renderer. One value —
    /// assign a rebuilt list (a <see cref="GizmoRenderer"/>'s <c>Build()</c>) to redraw.</summary>
    [EngineSync(Converter = typeof(GizmoOpsConverter))]
    public partial IReadOnlyList<GizmoOp> Ops { get; set; }

    /// <summary>Whether the engine renders the layer; toggling costs nothing — the drawing stays
    /// resident engine-side.</summary>
    [EngineSync]
    public partial bool IsVisible { get; set; }

    /// <summary>The engine view name the layer is restricted to (a <c>ViewportStream.ViewName</c>);
    /// empty renders in every editor view.</summary>
    [EngineSync]
    public partial string View { get; set; }

    /// <summary>The wire address for a layer name — the class attribute's Address template, spelled
    /// once more for senders that address a layer without an instance (the hub's re-push/remove).</summary>
    internal static string AddressFor(string name) => $"gizmos/{name}";
}
