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

    // Builds an in-memory material that shows a texture on a plane, for the asset preview of a raw
    // texture; replies with the created material's handle id.
    public const string EditorPreviewTextureMaterial = "editor.previewTextureMaterial";

    public const string EnginePing = "engine.ping";
    public const string EngineShutdown = "engine.shutdown";
    public const string EngineSetPaused = "engine.setPaused";
    public const string EngineSetPlaying = "engine.setPlaying";

    // Advances the paused simulation by exactly one fixed tick (the game view's next-frame button).
    public const string EngineStep = "engine.step";
    public const string EngineSetLogColors = "engine.setLogColors";
    public const string EngineLog = "engine.log";

    // The engine-sync channel (see EngineObject/SyncHub): family commands carry value pushes, the
    // uniform sync.* verbs carry the property grid's default-tracking, and sync.changed is the engine's
    // inbound delta. The engine-side handlers land together with world sync.
    public const string SyncChanged = "sync.changed";
    public const string SyncDescribe = "sync.describe";
    public const string SyncReset = "sync.reset";
    public const string SyncIsDefault = "sync.isDefault";

    // Engine-to-editor notification bracketing one interactive viewport edit (a gizmo drag): a
    // { phase: "begin" } as the drag starts and a { phase: "commit" } when it lands. The focused world
    // owner coalesces the synced changes streamed between them into a single undo step and flags the
    // world dirty on commit.
    public const string EditTransaction = "edit.transaction";

    // The engine-sync event channel (see EngineObject's partial events): subscribe/unsubscribe tell the
    // engine which (address, key) raises to stream — driven by an event's first/last handler — and
    // sync.event is the engine's inbound raise.
    public const string SyncEvent = "sync.event";
    public const string SyncSubscribe = "sync.subscribe";
    public const string SyncUnsubscribe = "sync.unsubscribe";

    // The uniform world-qualified property write: world/{w}/entities/{id}/{scalar} for an entity field,
    // world/{w}/entities/{id}/components/{comp}/{key} for a component property (w=0 is the active world).
    // Every entity-scalar and component edit — from the typed Entity/Component mirrors and from
    // PreviewWorld's isolated-world driver alike — pushes through this; the engine has no
    // entity.set/component.set handlers.
    public const string SyncSet = "sync.set";

    // The runtime physics surface: raycast is a typed-reply query; overlapScan requests a manual
    // trigger scan by component address. The trigger/collider event raises ride the sync.event channel.
    public const string PhysicsRaycast = "physics.raycast";
    public const string PhysicsOverlapScan = "physics.overlapScan";

    // Asset field edits ride the uniform sync.set path (path-addressed asset/{id}/{property}); assets no
    // longer have their own set verb. asset.create/save remain the identity + persistence verbs.
    public const string AssetCreate = "asset.create";
    public const string AssetSave = "asset.save";

    // Force-registers an existing asset file by path into the engine registry (establishing its id→path
    // mapping) so a later id reference to it resolves; replies { id }. The editor uses this to register the
    // specific assets it wants — e.g. the asset-preview world's bundled dependencies.
    public const string AssetLoad = "asset.load";

    /// <summary>Drops an asset from the engine's in-memory registry after its files are deleted from disk, so a
    /// catalog refresh reflects the delete immediately instead of racing the engine's own file scan.</summary>
    public const string AssetForget = "asset.forget";

    /// <summary>Mints a fresh asset id, used to stamp a duplicate's <c>.meta</c> so the copy is its own asset
    /// rather than an alias of the original.</summary>
    public const string AssetNewId = "asset.newId";

    public const string WorldOpen = "world.open";

    // Loads a standalone world alongside the active one (by { path } for a bundled resource, or { assetId }
    // for a project world) and returns its { worldAssetId }; world.close releases it. The asset viewer uses
    // these to own the lifetime of its preview world.
    public const string WorldLoad = "world.load";
    public const string WorldClose = "world.close";

    // Loads the active world's entities (each with its full components and an is-global flag) and writes
    // the active world back to disk — the editor's live-world read and save (see world_ops).
    public const string WorldDescribe = "world.describe";
    public const string WorldSave = "world.save";

    public const string EntityCreate = "entity.create";
    public const string EntityDescribe = "entity.describe";
    public const string EntityDuplicate = "entity.duplicate";
    public const string EntityDestroy = "entity.destroy";
    public const string EntityMove = "entity.move";
    public const string EntityAddComponent = "entity.addComponent";
    public const string EntityRemoveComponent = "entity.removeComponent";
    public const string EntityAddScript = "entity.addScript";

    public const string ViewStart = "view.start";
    public const string ViewStop = "view.stop";
    public const string ViewSurface = "view.surface";
    public const string ViewPresented = "view.presented";
    public const string ViewInput = "view.input";

    // Frames an asset-preview view's orbit camera to the renderable bounds the editor built in its
    // isolated world; called once the previewed entity exists.
    public const string ViewFrameAssetPreview = "view.frameAssetPreview";

    // The transform gizmo (Worlds/GizmoTool): the active tool's editor-authored handle set + snap
    // settings, pushed as one notification.
    public const string ViewSetGizmo = "view.setGizmo";

    // Viewport picking: the entity under a view's normalized point, or {gizmo: true} when the
    // cursor is on a transform handle (neither select nor clear).
    public const string ViewPick = "view.pick";

    // Projects every entity in a view's world to its normalized screen position + camera distance
    // ({ items: [{ id, u, v, depth }] }); the node overlay polls this per presented frame to place and
    // distance-scale its nodes.
    public const string ViewProjectEntities = "view.projectEntities";

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

    /// <summary>Registers an extra asset search root at startup (repeatable). The editor points the engine
    /// at the project root with this so the project's <c>Assets/</c> resolve even though the settings file
    /// now lives in <c>.toybox</c> (the engine otherwise only roots assets at the settings file's folder).</summary>
    public const string RegisterAssetsArgument = "--register-assets";

    public const string HiddenArgument = "--hidden";
    public const string InjectPluginsArgument = "--inject-plugins";
    public const string LifetimeLinkArgument = "--live-together-die-together";
    public const string RpcPortVariable = "TBX_STUDIO_RPC_PORT";
}
