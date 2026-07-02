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
