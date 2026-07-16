namespace Toybox.Studio.WorldViewer;

/// <summary>
/// The world viewport's transform tools, as the toolbar and keybindings pick them. Editor-side
/// vocabulary only — the wire carries each mode's handle set (see <see cref="TransformGizmo"/>), never
/// a mode.
/// </summary>
public enum GizmoMode
{
    /// <summary>The default tool: click-picking, with the combined "super" gizmo (translate +
    /// rotate + scale handles together) on the selection.</summary>
    Select,

    Translate,

    Rotate,

    Scale,
}
