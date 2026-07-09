using Toybox.Studio.Settings;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.SettingsEditor;

/// <summary>
/// A detached, grid-editable copy of the project's <see cref="AppSettings"/> mirror. The mirror's
/// nested values are immutable records edited by reassignment (that is what pushes them), which a
/// reflection grid can't do member-by-member — so the grid edits this draft's own record copies, and
/// <see cref="ApplyTo"/> lands each section back on the mirror as one whole-record assignment (one
/// push each). The icon handle stays out — there is no handle editor in the grid yet.
/// </summary>
public sealed class AppSettingsDraft
{
    [Icon("Monitor")]
    public GraphicsSettings Graphics { get; set; } = new();

    [Icon("Earth")]
    public WorldSettings World { get; set; } = new();

    [Icon("Atom")]
    public PhysicsSettings Physics { get; set; } = new();

    [Icon("Timer")]
    public AsyncSettings Async { get; set; } = new();

    public string AppName { get; set; } = "";

    public List<string> Plugins { get; set; } = [];

    public static AppSettingsDraft From(AppSettings app) => new()
    {
        // Record copies, so grid edits never touch the instances the live mirror still holds.
        Graphics = app.Graphics with { },
        World = app.World with { },
        Physics = app.Physics with { },
        Async = app.Async with { },
        AppName = app.AppName,
        Plugins = [.. app.Plugins],
    };

    /// <summary>Lands the draft on the live mirror — whole-record assignments, so every changed
    /// section pushes to the engine like any other asset edit.</summary>
    public void ApplyTo(AppSettings app)
    {
        app.Graphics = Graphics;
        app.World = World;
        app.Physics = Physics;
        app.Async = Async;
        app.AppName = AppName;
        app.Plugins = Plugins;
    }
}
