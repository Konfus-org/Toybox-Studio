using Newtonsoft.Json;

namespace Toybox.Studio.Settings;

/// <summary>
/// Owns the editor's persisted configuration for the app's lifetime. The <see cref="EditorSettings"/>
/// data itself is plain; this manager does all the persistence — it loads EditorSettings.json from the
/// user's .toybox folder once at construction (falling back to defaults, preserving an unreadable file
/// as a *.corrupt breadcrumb) and writes it back on <see cref="SaveAsync"/>. Mutate the object graph
/// under <see cref="Settings"/>, then save.
/// </summary>
public sealed class SettingsManager
{
    /// <summary>
    /// The root .toybox folder under the user profile where all editor data lives (settings,
    /// themes, logs). Use this instead of hard-coding ".toybox" elsewhere.
    /// </summary>
    public static string BaseDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".toybox");

    private static readonly string FilePath = Path.Combine(BaseDirectory, "EditorSettings.json");

    public SettingsManager() => Settings = Load();

    public EditorSettings Settings { get; }

    /// <summary>
    /// Writes the current settings back to EditorSettings.json without blocking the calling (UI) thread:
    /// the JSON is serialized synchronously on the caller (a correct, race-free snapshot) and only the disk
    /// write is awaited. Callers either <c>await</c> this (a future Settings panel) or fire-and-forget it
    /// (the event-driven service writes — theme pick, recent-project list).
    /// </summary>
    public async Task SaveAsync()
    {
        Directory.CreateDirectory(BaseDirectory);
        var json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
        await File.WriteAllTextAsync(FilePath, json).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the settings from EditorSettings.json, falling back to defaults when the file is missing
    /// or unreadable.
    /// </summary>
    private static EditorSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonConvert.DeserializeObject<EditorSettings>(File.ReadAllText(FilePath)) is { } loaded)
                return loaded;
        }
        catch (Exception)
        {
            // Corrupt settings fall back to defaults. Loaded before the logger exists, so preserve the
            // unreadable file as a visible breadcrumb instead of letting the next Save() silently destroy
            // the user's (possibly recoverable) customizations.
            PreserveCorruptFile();
        }

        return new EditorSettings();
    }

    private static void PreserveCorruptFile()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Move(FilePath, FilePath + ".corrupt", overwrite: true);
        }
        catch (Exception)
        {
            // Best-effort; if it can't be moved aside, the next Save() overwrites it anyway.
        }
    }
}
