namespace Toybox.Studio.EngineApi;

/// <summary>
/// The engine's JSON-RPC method names in one place — the wire protocol surface the editor calls. Domain
/// constructs and the engine facade reference these constants instead of re-spelling the string at each call
/// site, so the protocol is enumerable and a typo is a compile error rather than a silent unhandled-method
/// failure. Grouped by engine namespace (editor./engine./world./entity./asset./sync./view./app.).
/// </summary>
public static class EngineMethods
{
    public const string EditorHello = "editor.hello";
    public const string EditorListAssets = "editor.listAssets";
    public const string EditorModelSlots = "editor.modelSlots";
    public const string EditorPreviewTextureMaterial = "editor.previewTextureMaterial";
    public const string EditorLog = "editor.log";

    public const string EnginePing = "engine.ping";
    public const string EngineShutdown = "engine.shutdown";
    public const string EngineSetPaused = "engine.setPaused";
    public const string EngineSetPlaying = "engine.setPlaying";
    public const string EngineSetLogColors = "engine.setLogColors";
    public const string EngineLog = "engine.log";

    public const string WorldDescribe = "world.describe";
    public const string WorldSave = "world.save";
    public const string WorldOpen = "world.open";

    public const string EntityCreate = "entity.create";
    public const string EntityDescribe = "entity.describe";
    public const string EntityDestroy = "entity.destroy";
    public const string EntityMove = "entity.move";
    public const string EntitySetComponent = "entity.setComponent";
    public const string EntityRemoveComponent = "entity.removeComponent";
    public const string EntityAddComponent = "entity.addComponent";
    public const string EntityAddScript = "entity.addScript";

    public const string AssetDescribe = "asset.describe";
    public const string AssetSave = "asset.save";
    public const string AssetCreate = "asset.create";
    public const string AssetForget = "asset.forget";
    public const string AssetNewId = "asset.newId";
    public const string AssetPreviewStats = "asset.previewStats";
    public const string AssetPairing = "asset.pairing";
    public const string AssetGenerateMissingMetas = "asset.generateMissingMetas";

    public const string SyncSet = "sync.set";
    public const string SyncReset = "sync.reset";
    public const string SyncIsDefault = "sync.isDefault";
    public const string SyncDescribe = "sync.describe";
    public const string SyncCatalog = "sync.catalog";

    public const string ViewStart = "view.start";
    public const string ViewStop = "view.stop";
    public const string ViewSurface = "view.surface";
    public const string ViewPresented = "view.presented";
    public const string ViewInput = "view.input";
    public const string ViewPick = "view.pick";
    public const string ViewPickRect = "view.pickRect";
    public const string ViewSetSelection = "view.setSelection";
    public const string ViewProjectEntities = "view.projectEntities";
    public const string ViewQueryOcclusion = "view.queryOcclusion";
    public const string ViewTransformEdited = "view.transformEdited";
    public const string ViewSetGizmo = "view.setGizmo";
    public const string ViewFrameAssetPreview = "view.frameAssetPreview";

    public const string AppDescribeSettings = "app.describeSettings";
}
