namespace Toybox.Studio.Keybindings;

/// <summary>
/// The editor's action-id vocabulary — the single place ids are spelled. An id is an action's stable
/// identity everywhere: its <see cref="EditorAction"/> registration, its <see cref="InputAction"/>
/// entry in the editor keymap, and the <see cref="EditorActionInvoked"/> events that run it. Never
/// re-spell one as a string literal.
/// </summary>
public static class ActionIds
{
    public const string OpenSettings = "edit.openSettings";

    public const string BuildAppDebug = "build.app.debug";
    public const string BuildAppRelease = "build.app.release";
    public const string BuildEngine = "build.engine";

    public const string AttachToRunningBuild = "debug.attachToRunningBuild";

    public const string SaveLayout = "layout.save";
    public const string LoadLayout = "layout.load";
    public const string ResetLayout = "layout.reset";

    // The viewport transform tools (scoped to the focused viewport; see ActionSchemes.Viewport).
    public const string GizmoSelect = "gizmo.select";
    public const string GizmoTranslate = "gizmo.translate";
    public const string GizmoRotate = "gizmo.rotate";
    public const string GizmoScale = "gizmo.scale";
    public const string GizmoSnapHold = "gizmo.snapHold";
    public const string GizmoToggleSnap = "gizmo.toggleSnap";

    // The viewport render layers (the collider wireframe toggles, the post-processing toggle, and
    // the render-stage radio row; see Worlds/RenderLayers).
    public const string RenderLayersCollidersAll = "renderLayers.collidersAll";
    public const string RenderLayersCollidersOnSelection = "renderLayers.collidersOnSelection";
    public const string RenderLayersPostProcessing = "renderLayers.postProcessing";
    public const string RenderLayersStageFinal = "renderLayers.stage.final";
    public const string RenderLayersStageDiffuse = "renderLayers.stage.diffuse";
    public const string RenderLayersStageNormals = "renderLayers.stage.normals";
    public const string RenderLayersStageShadows = "renderLayers.stage.shadows";
    public const string RenderLayersStageDepth = "renderLayers.stage.depth";

    /// <summary>The open-a-panel family: one action per registered dockable, keyed by the dockable's
    /// identity (its view-model type name).</summary>
    public static string OpenWindow(string dockableKey) => $"window.{dockableKey}.open";
}
