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

    public const string ComponentSet = "component.set";

    public const string EntitySet = "entity.set";
    public const string EntityDuplicate = "entity.duplicate";
    public const string EntityDestroy = "entity.destroy";
    public const string EntityAddComponent = "entity.addComponent";

    public const string ViewStart = "view.start";
    public const string ViewStop = "view.stop";
    public const string ViewSurface = "view.surface";
    public const string ViewPresented = "view.presented";
    public const string ViewInput = "view.input";

    // The launcher process's command-line switches and environment (see Engine.Launch).
    public const string AppArgument = "--app";
    public const string SettingsArgument = "--settings";
    public const string HiddenArgument = "--hidden";
    public const string InjectPluginsArgument = "--inject-plugins";
    public const string LifetimeLinkArgument = "--live-together-die-together";
    public const string RpcPortVariable = "TBX_STUDIO_RPC_PORT";
}
