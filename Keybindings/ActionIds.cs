namespace Toybox.Studio.Keybindings;

/// <summary>
/// The editor's action-id vocabulary — the single place ids are spelled. An id is an action's stable
/// identity everywhere: its <see cref="EditorAction"/> registration, its <see cref="InputAction"/>
/// entry in the editor keymap, and the <see cref="EditorActionInvoked"/> events that run it. Never
/// re-spell one as a string literal.
/// </summary>
public static class ActionIds
{
    // The File menu: save the focused document, switch the active project (in-process), open an asset, open a
    // raw source file (a script or shader) in the Coder editor.
    public const string Save = "file.save";
    public const string OpenProject = "file.openProject";
    public const string OpenAsset = "file.openAsset";
    public const string OpenSource = "file.openSource";

    public const string OpenSettings = "edit.openSettings";

    // Undo/redo of the focused document (see Workspace.Focused / IUndoTarget) — global chords routed to
    // whichever panel has focus.
    public const string Undo = "edit.undo";
    public const string Redo = "edit.redo";

    public const string BuildAppDebug = "build.app.debug";
    public const string BuildAppRelease = "build.app.release";
    public const string BuildEngine = "build.engine";

    public const string AttachToRunningBuild = "debug.attachToRunningBuild";

    public const string SaveLayout = "layout.save";
    public const string LoadLayout = "layout.load";
    public const string ResetLayout = "layout.reset";

    // The World Tree's entity edit verbs (scoped to the focused hierarchy panel; registered by
    // WorldTree's WorldTreeActions under Scheme.For<WorldTreeViewModel>(), executed by the panel through
    // EntityOperations). Also shown as rows in the Edit menu.
    public const string EntityCopy = "edit.entity.copy";
    public const string EntityCut = "edit.entity.cut";
    public const string EntityPaste = "edit.entity.paste";
    public const string EntityDuplicate = "edit.entity.duplicate";
    public const string EntityDelete = "edit.entity.delete";
    public const string EntityRename = "edit.entity.rename";

    // The world viewport transform tools (scoped to the focused world viewport; registered by
    // WorldViewer's WorldViewerToolbarActions under Scheme.For<WorldViewerViewModel>()).
    public const string GizmoSelect = "gizmo.select";
    public const string GizmoTranslate = "gizmo.translate";
    public const string GizmoRotate = "gizmo.rotate";
    public const string GizmoScale = "gizmo.scale";
    public const string GizmoSnapHold = "gizmo.snapHold";
    public const string GizmoToggleSnap = "gizmo.toggleSnap";
    public const string GizmoToggleOrientation = "gizmo.toggleOrientation";

    // The world viewport render layers (the collider wireframe toggles, the post-processing toggle,
    // and the render-stage radio row; see WorldViewer/RenderLayers).
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
