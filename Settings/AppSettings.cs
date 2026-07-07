using Toybox.Studio.Assets;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Settings;

/// <summary>
/// The open project's <c>AppSettings.json</c>, mirrored from the engine's <c>AppSettings</c> asset:
/// the per-system settings blocks (graphics, world, physics, async) plus the app's icon, display name,
/// and plugin list. Not <see cref="CreatableAttribute">creatable</see> — the file is authored by
/// project creation (its presence is what makes a folder a project) and every project already has
/// exactly one. The <see cref="SettingsManager"/> owns the loaded mirror.
/// </summary>
public sealed partial class AppSettings : Asset
{
    /// <summary>The settings file every project carries at its root; matches the engine registry's
    /// normalized path and <c>ProjectLoader.SettingsFileName</c>.</summary>
    public const string FileName = "AppSettings.json";

    /// <summary>The load of the project's existing settings asset (see <see cref="Asset.Loaded"/>).</summary>
    public AppSettings(ulong id = 0) : base(id) => InitializeDefaults();

    [EngineSync(Converter = typeof(GraphicsSettingsConverter))]
    public partial GraphicsSettings Graphics { get; set; }

    [EngineSync(Converter = typeof(WorldSettingsConverter))]
    public partial WorldSettings World { get; set; }

    [EngineSync(Converter = typeof(PhysicsSettingsConverter))]
    public partial PhysicsSettings Physics { get; set; }

    [EngineSync(Converter = typeof(AsyncSettingsConverter))]
    public partial AsyncSettings Async { get; set; }

    /// <summary>The app's window/taskbar icon (a texture asset); the engine falls back to its built-in
    /// Toybox icon while the handle is unset.</summary>
    [EngineSync]
    public partial Handle Icon { get; set; }

    /// <summary>The app's display name (the window title). Wired to the engine's <c>name</c> field —
    /// <see cref="Asset.Name"/> is this asset's file identity, which is a different thing.</summary>
    [EngineSync(Key = "name")]
    public partial string AppName { get; set; }

    /// <summary>The plugins the app loads, in load order.</summary>
    [EngineSync(Converter = typeof(StringListConverter))]
    public partial IReadOnlyList<string> Plugins { get; set; }

    // The engine's own defaults, so an unbound (or not-yet-hydrated) mirror reads as a default app.
    private void InitializeDefaults()
    {
        Graphics = new GraphicsSettings();
        World = new WorldSettings();
        Physics = new PhysicsSettings();
        Async = new AsyncSettings();
        AppName = "Toybox App";
        Plugins =
        [
            "PerformanceMonitor",
            "SdlInput",
            "JoltPhysics",
            "SdlWindowing",
            "SdlOpenGlContextManager",
            "OpenGlRendering",
            "StbImageLoader",
            "AssimpModelLoader",
            "ShaderIncludeLoader",
        ];
    }
}
