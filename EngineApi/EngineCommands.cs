namespace Toybox.Studio.EngineApi;

/// <summary>
/// Every constant the editor uses to command the engine, in one place: the JSON-RPC method names (the
/// wire protocol surface, grouped by engine namespace: editor./engine./view.) and the launcher process's
/// command-line/environment vocabulary. Call sites reference these constants instead of re-spelling the
/// string, so the protocol is enumerable and a typo is a compile error rather than a silent
/// unhandled-method failure.
/// </summary>
public static class EngineCommands
{
    public const string EditorHello = "editor.hello";
    public const string EditorLog = "editor.log";
    public const string EditorListAssets = "editor.listAssets";

    public const string EnginePing = "engine.ping";
    public const string EngineShutdown = "engine.shutdown";
    public const string EngineSetPaused = "engine.setPaused";
    public const string EngineSetPlaying = "engine.setPlaying";
    public const string EngineSetLogColors = "engine.setLogColors";
    public const string EngineLog = "engine.log";

    // The engine-sync channel (see EngineObject/SyncHub): family commands carry value pushes, the
    // uniform sync.* verbs carry the property grid's default-tracking, and sync.changed is the engine's
    // inbound delta. The engine-side handlers land together with world sync.
    public const string SyncChanged = "sync.changed";
    public const string SyncDescribe = "sync.describe";
    public const string SyncReset = "sync.reset";
    public const string SyncIsDefault = "sync.isDefault";

    // The engine-sync event channel (see EngineObject's partial events): subscribe/unsubscribe tell the
    // engine which (address, key) raises to stream — driven by an event's first/last handler — and
    // sync.event is the engine's inbound raise.
    public const string SyncEvent = "sync.event";
    public const string SyncSubscribe = "sync.subscribe";
    public const string SyncUnsubscribe = "sync.unsubscribe";

    public const string ComponentSet = "component.set";

    // The runtime physics surface: raycast is a typed-reply query; overlapScan requests a manual
    // trigger scan by component address. The trigger/collider event raises ride the sync.event channel.
    public const string PhysicsRaycast = "physics.raycast";
    public const string PhysicsOverlapScan = "physics.overlapScan";

    public const string AssetSet = "asset.set";
    public const string AssetCreate = "asset.create";
    public const string AssetSave = "asset.save";

    public const string EntitySet = "entity.set";
    public const string EntityDuplicate = "entity.duplicate";
    public const string EntityDestroy = "entity.destroy";
    public const string EntityAddComponent = "entity.addComponent";

    public const string ViewStart = "view.start";
    public const string ViewStop = "view.stop";
    public const string ViewSurface = "view.surface";
    public const string ViewPresented = "view.presented";
    public const string ViewInput = "view.input";

    // The transform gizmo (Worlds/GizmoTool): the active tool's editor-authored handle set + snap
    // settings, pushed as one notification.
    public const string ViewSetGizmo = "view.setGizmo";

    // Viewport picking: the entity under a view's normalized point, or {gizmo: true} when the
    // cursor is on a transform handle (neither select nor clear).
    public const string ViewPick = "view.pick";

    // The editor-authored gizmo overlay (the Gizmos project): retained named layers of drawing ops
    // the engine replays into its gizmo renderer over editor viewports.
    public const string GizmoSet = "gizmos.set";
    public const string GizmoRemove = "gizmos.remove";

    // The editor's entity selection (Ecs/WorldSelection): the selected-id set the engine highlights,
    // pushed as one synced value.
    public const string SelectionSet = "selection.set";

    // The render layers (Worlds/RenderLayers): the collider wireframe modes, the post-processing
    // toggle, and the render-stage debug view, pushed as one notification.
    public const string EditorSetRenderLayers = "editor.setRenderLayers";

    // The launcher process's command-line switches and environment (see Engine.Launch).
    public const string AppArgument = "--app";
    public const string SettingsArgument = "--settings";
    public const string HiddenArgument = "--hidden";
    public const string InjectPluginsArgument = "--inject-plugins";
    public const string LifetimeLinkArgument = "--live-together-die-together";
    public const string RpcPortVariable = "TBX_STUDIO_RPC_PORT";
}
