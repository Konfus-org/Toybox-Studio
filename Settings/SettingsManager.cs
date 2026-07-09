using Newtonsoft.Json;
using Toybox.Studio.Assets;
using Toybox.Studio.Events;

namespace Toybox.Studio.Settings;

/// <summary>
/// Owns the editor's persisted configuration for the app's lifetime, in two halves. The editor half is
/// plain data: <see cref="Editor"/> loads from EditorSettings.json in the user's .toybox folder once at
/// construction (falling back to defaults, preserving an unreadable file as a *.corrupt breadcrumb) and
/// writes back on <see cref="SaveAsync"/> — mutate the object graph, then save; each save dispatches one
/// <see cref="EditorSettingsChanged"/>. The project half is an
/// asset: <see cref="App"/> is the open project's <see cref="AppSettings"/> mirror, loaded from the
/// asset catalog's <c>AppSettings.json</c> row as the engine connection comes up and dropped when it
/// goes; edits push live like any asset's and persist through the asset's own
/// <see cref="Asset.SaveAsync"/>. Each swap of <see cref="App"/> dispatches one
/// <see cref="AppSettingsChanged"/>.
/// </summary>
public sealed class SettingsManager : EventSubscriber, IEventHandler<AssetCatalogChanged>
{
    /// <summary>
    /// The root .toybox folder under the user profile where all editor data lives (settings,
    /// themes, logs). Use this instead of hard-coding ".toybox" elsewhere.
    /// </summary>
    public static string BaseDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".toybox");

    private static readonly string FilePath = Path.Combine(BaseDirectory, "EditorSettings.json");

    public SettingsManager(EventDispatcher events) : base(events) => Editor = Load();

    public EditorSettings Editor { get; }

    /// <summary>The open project's app settings asset, live while the catalog advertises it (the
    /// engine connection is up); null otherwise. Await its <see cref="Asset.Loaded"/> to know the
    /// values are hydrated.</summary>
    public AppSettings? App { get; private set; }

    /// <summary>Tracks the project's <c>AppSettings.json</c> through the catalog: the row appearing
    /// (or changing identity) loads a fresh mirror, the listing emptying on disconnect drops it.
    /// Catalog paths are the engine registry's absolute normalized paths, so the row is matched by
    /// file name — skipping build-output copies, which shadow the source file.</summary>
    public void Handle(in AssetCatalogChanged evt)
    {
        var entry = evt.Entries.FirstOrDefault(candidate =>
            string.Equals(
                Path.GetFileName(candidate.Path), AppSettings.FileName, StringComparison.OrdinalIgnoreCase)
            && !candidate.Path.Contains("/build/", StringComparison.OrdinalIgnoreCase));
        if (App?.Id == entry?.Id)
            return;

        // Detach the outgoing mirror so the hub doesn't keep routing to (or warn about) a settings
        // object nobody holds anymore.
        App?.Unbind();
        App = entry is null ? null : new AppSettings(entry.Id);
        Events.Dispatch(new AppSettingsChanged(App));
    }

    /// <summary>
    /// Writes the current editor settings back to EditorSettings.json without blocking the calling (UI)
    /// thread: the JSON is serialized synchronously on the caller (a correct, race-free snapshot) and only
    /// the disk write is awaited. Callers either <c>await</c> this (a future Settings panel) or
    /// fire-and-forget it (the event-driven service writes — theme pick, recent-project list). Editing is
    /// mutate-then-save, so the save is where the change becomes announceable: each call dispatches one
    /// <see cref="EditorSettingsChanged"/> — on the caller's thread, before the disk write, since the
    /// in-memory values consumers react to have changed whether or not the write succeeds. The project's
    /// <see cref="App"/> asset persists separately, through its own <see cref="Asset.SaveAsync"/>.
    /// </summary>
    public async Task SaveAsync()
    {
        var json = JsonConvert.SerializeObject(Editor, Formatting.Indented);
        Events.Dispatch(new EditorSettingsChanged(Editor));

        Directory.CreateDirectory(BaseDirectory);
        await File.WriteAllTextAsync(FilePath, json).ConfigureAwait(false);
    }

    /// <summary>
    /// A detached deep copy of the current editor settings, for a buffered edit (the Settings window):
    /// mutate the draft freely, then land it with <see cref="ApplyEditorDraftAsync"/> — or just drop it.
    /// </summary>
    public EditorSettings CreateEditorDraft() =>
        JsonConvert.DeserializeObject<EditorSettings>(JsonConvert.SerializeObject(Editor))!;

    /// <summary>
    /// Commits a draft: copies its values into the live <see cref="Editor"/> object (whose identity
    /// services hold), then persists through <see cref="SaveAsync"/> — dispatching the usual
    /// <see cref="EditorSettingsChanged"/>. Creation is Replace, not the default merge, so the draft's
    /// list edits don't append onto the live lists.
    /// </summary>
    public Task ApplyEditorDraftAsync(EditorSettings draft)
    {
        JsonConvert.PopulateObject(
            JsonConvert.SerializeObject(draft),
            Editor,
            new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
        return SaveAsync();
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
