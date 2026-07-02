using Newtonsoft.Json;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Shell;

/// <summary>
/// The editor's persisted settings — currently just what locating/launching the engine needs — stored
/// as ~/.toybox/StudioSettings.json. Loaded once at startup; <see cref="SaveAsync"/> writes it back
/// (the engine locator saves after discovering the engine source path). Consumers take the plain
/// values they need (e.g. via <see cref="Toybox.Studio.EngineApi.EngineLaunchInfo"/>) rather than
/// holding this type.
/// </summary>
public sealed class StudioSettings
{
    private static readonly string FilePath = Path.Combine(AppData.BaseDirectory, "StudioSettings.json");

    public string SourcePath { get; set; } = "";

    /// <summary>The engine's own OS window stays hidden; the editor's viewport is the only view.</summary>
    public bool HideEngineWindow { get; set; } = true;

    public int ConnectTimeoutSeconds { get; set; } = 30;

    public bool RestartOnCrash { get; set; } = true;

    /// <summary>Loads the saved settings, or defaults when the file is missing or unreadable.</summary>
    public static StudioSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonConvert.DeserializeObject<StudioSettings>(File.ReadAllText(FilePath)) is { } settings)
                return settings;
        }
        catch (Exception)
        {
            // A malformed settings file must never block startup; fall back to defaults.
        }

        return new StudioSettings();
    }

    public async Task SaveAsync()
    {
        Directory.CreateDirectory(AppData.BaseDirectory);
        await File.WriteAllTextAsync(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented))
            .ContinueOnAnyContext();
    }
}
