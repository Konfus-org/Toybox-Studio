using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Gizmos;

/// <summary>
/// A transform handle's interaction semantics — the only vocabulary the engine keeps about the gizmo.
/// What a handle looks like is authored here in the editor (its drawing ops); what dragging it does is
/// its kind.
/// </summary>
public enum GizmoHandleKind
{
    /// <summary>Dragging translates along the handle's axis.</summary>
    Arrow,

    /// <summary>Dragging rotates about the handle's axis.</summary>
    Ring,

    /// <summary>Dragging scales the handle's axis.</summary>
    Knob,

    /// <summary>Dragging scales uniformly on every axis.</summary>
    Center,

    /// <summary>Dragging translates in the plane whose normal is the handle's axis (two axes at once).</summary>
    Plane,

    /// <summary>Dragging scales the two axes in the plane whose normal is the handle's axis.</summary>
    PlaneScale,
}
