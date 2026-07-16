namespace Toybox.Studio.WorldViewer;

/// <summary>
/// The transform gizmo's axis frame: <see cref="Global"/> aligns the handles to the world axes;
/// <see cref="Local"/> aligns them to the primary selected entity's own rotation (the engine derives
/// the basis from that entity per frame). Editor-side vocabulary only — the wire carries "global" or
/// "local" (see <see cref="WorldViewerTool"/>).
/// </summary>
public enum GizmoOrientation
{
    Global,

    Local,
}
