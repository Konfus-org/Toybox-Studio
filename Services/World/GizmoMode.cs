namespace Toybox.Studio.Services.World;

/// <summary>The active viewport transform tool. <see cref="None"/> is the select tool (no gizmo; a
/// left-drag box-selects). The others show the engine's transform gizmo.</summary>
public enum GizmoMode
{
    None,
    Translate,
    Rotate,
    Scale,
}
