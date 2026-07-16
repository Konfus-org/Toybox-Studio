using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Gizmos;

/// <summary>Which world axis a transform-gizmo handle acts on; <see cref="All"/> is the uniform-scale
/// centre handle's "every axis"; <see cref="View"/> is the camera-facing axis for the screen-space
/// rotate ring (the engine derives it per frame from the camera).</summary>
public enum GizmoAxis
{
    X,
    Y,
    Z,
    All,

    /// <summary>The camera-facing axis (screen-space) — the rotate gizmo's outer view ring.</summary>
    View,
}
