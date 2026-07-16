using Newtonsoft.Json;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.Events;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Settings;

/// <summary>
/// Owns the editor's persisted configuration for the app's lifetime, in two halves. The editor half is
/// plain data: <see cref="Editor"/> loads from EditorSettings.json in the user's .toybox folder once at
/// construction (falling back to defaults, preserving an unreadable file as a *.corrupt breadcrumb) and
/// writes back on <see cref="ApplyAsync"/> — mutate the live object graph directly, then apply; each apply
/// dispatches one <see cref="EditorSettingsChanged"/>. Its project-scoped sections (Build, Gizmos,
/// EditorAssetSettings) split off into the open project's own <c>.toybox/ProjectSettings.json</c>: they
/// are loaded onto the live <see cref="Editor"/> by <see cref="LoadProjectSettings"/> once a project is
/// open (and re-loaded on an in-process switch) and written by <see cref="ApplyProjectAsync"/>, so the
/// grid and every consumer still edit them through <see cref="Editor"/>. The project half is an
/// asset: <see cref="App"/> is the open project's <see cref="AppSettings"/> mirror, loaded from the
/// asset catalog's <c>AppSettings.json</c> row as the engine connection comes up and dropped when it
/// goes; edits push live like any asset's and persist through the asset's own
/// <see cref="Asset.SaveAsync"/>. Each swap of <see cref="App"/> dispatches one
/// <see cref="AppSettingsChanged"/>.
/// </summary>
public sealed class SettingsManager : EventSubscriber, IEventHandler<AssetCatalogChanged>
{
    private readonly PathsCatalog _paths;
    private readonly ProjectPaths _projectPaths;

    // Coalesces overlapping disk writes: only one runs at a time, and the freshest snapshot always
    // wins. A snap-amount scrub fires one ApplyAsync per step — without this those fire-and-forget
    // writes would collide on the file. Exchanged (not locked) so a save posted mid-write is still seen.
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private string? _pendingJson;

    // The same coalescing, for the project-scoped ProjectSettings.json — a separate gate so a project
    // write and a global write can't block one another.
    private readonly SemaphoreSlim _projectWriteGate = new(1, 1);
    private string? _pendingProjectJson;

    // Deserialize collections by REPLACING each property's initializer default, not appending onto it.
    // Newtonsoft's default (Auto) POPULATES an already-initialized list, so a loaded EditorSettings.json
    // would otherwise stack its saved categories on top of EditorAssetSettings' defaults — compounding on
    // every launch (the source of the Asset Browser's duplicated category rail).
    private static readonly JsonSerializerSettings ReplaceCollections =
        new() { ObjectCreationHandling = ObjectCreationHandling.Replace };

    public SettingsManager(
        EventDispatcher events, PathsCatalog paths, ProjectPaths projectPaths) : base(events)
    {
        _paths = paths;
        _projectPaths = projectPaths;
        Editor = Load();
    }

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
    /// Persists the current editor settings to EditorSettings.json without blocking the calling (UI)
    /// thread: the JSON is serialized synchronously on the caller (a correct, race-free snapshot) and only
    /// the disk write is awaited. Callers either <c>await</c> this (the Settings panel's Save) or
    /// fire-and-forget it (the event-driven service writes — theme pick, recent-project list). Editing is
    /// mutate-the-live-graph-then-apply, so the apply is where the change becomes announceable: each call
    /// dispatches one <see cref="EditorSettingsChanged"/> — on the caller's thread, before the disk write,
    /// since the in-memory values consumers react to have changed whether or not the write succeeds. The
    /// project's <see cref="App"/> asset persists separately, through its own <see cref="Asset.SaveAsync"/>.
    /// </summary>
    public async Task ApplyAsync()
    {
        var json = JsonConvert.SerializeObject(Editor, Formatting.Indented);
        Events.Dispatch(new EditorSettingsChanged(Editor));

        // Post this snapshot as the pending write, then take a turn at the gate. Whoever holds the gate
        // flushes the freshest snapshot and clears it, so a burst of saves collapses to the latest —
        // and an awaiting caller still returns only once its snapshot (or a newer one) is on disk.
        Interlocked.Exchange(ref _pendingJson, json);
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Interlocked.Exchange(ref _pendingJson, null) is not { } pending)
                return;

            Directory.CreateDirectory(_paths.BaseDirectory);
            await File.WriteAllTextAsync(_paths.EditorSettingsFile, pending).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Loads the open project's project-scoped editor settings (Build, Gizmos, EditorAssetSettings) from
    /// its <c>.toybox/ProjectSettings.json</c> onto the live <see cref="Editor"/>, falling back to defaults
    /// when the file is missing or unreadable, and announces the change so live consumers re-read. Called by
    /// the launch flow once a project is loaded and by the in-process project switch when the project
    /// changes; a no-op before any project is open (the sections keep their defaults).
    /// </summary>
    public void LoadProjectSettings()
    {
        if (string.IsNullOrEmpty(_projectPaths.Root))
            return;

        var project = LoadProject();
        Editor.Build = project.Build;
        Editor.Gizmos = project.Gizmos;
        Editor.EditorAssetSettings = project.EditorAssetSettings;
        HealCategories(Editor);
        Events.Dispatch(new EditorSettingsChanged(Editor));
    }

    /// <summary>
    /// Persists the project-scoped editor sections to the open project's <c>.toybox/ProjectSettings.json</c>,
    /// mirroring <see cref="ApplyAsync"/>'s off-thread coalesced write (the JSON is snapshotted on the caller,
    /// only the disk write is awaited). A no-op before any project is open. Unlike <see cref="ApplyAsync"/>
    /// this does not dispatch: its callers (the Settings save, the gizmo toolbar's toggle/scrub) have already
    /// announced their edit through their own channel.
    /// </summary>
    public async Task ApplyProjectAsync()
    {
        if (string.IsNullOrEmpty(_projectPaths.Root))
            return;

        var project = new ProjectSettings
        {
            Build = Editor.Build,
            Gizmos = Editor.Gizmos,
            EditorAssetSettings = Editor.EditorAssetSettings,
        };
        var json = JsonConvert.SerializeObject(project, Formatting.Indented);

        Interlocked.Exchange(ref _pendingProjectJson, json);
        await _projectWriteGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Interlocked.Exchange(ref _pendingProjectJson, null) is not { } pending)
                return;

            Directory.CreateDirectory(_projectPaths.BaseDirectory);
            await File.WriteAllTextAsync(_projectPaths.ProjectSettingsFile, pending).ConfigureAwait(false);
        }
        finally
        {
            _projectWriteGate.Release();
        }
    }

    /// <summary>
    /// Loads the settings from EditorSettings.json, falling back to defaults when the file is missing
    /// or unreadable.
    /// </summary>
    private EditorSettings Load()
    {
        try
        {
            if (File.Exists(_paths.EditorSettingsFile)
                && JsonConvert.DeserializeObject<EditorSettings>(
                    File.ReadAllText(_paths.EditorSettingsFile), ReplaceCollections) is { } loaded)
                return loaded;
        }
        catch (Exception)
        {
            // Corrupt settings fall back to defaults. Loaded before the logger exists, so preserve the
            // unreadable file as a visible breadcrumb instead of letting the next Save() silently destroy
            // the user's (possibly recoverable) customizations.
            PreserveCorruptFile(_paths.EditorSettingsFile);
        }

        return new EditorSettings();
    }

    /// <summary>
    /// Reads the open project's <c>.toybox/ProjectSettings.json</c>, falling back to defaults when the file
    /// is missing or unreadable (an unreadable one moved aside as a *.corrupt breadcrumb, like the global
    /// file). Collections are replaced, not populated, so a loaded file can't stack onto the defaults.
    /// </summary>
    private ProjectSettings LoadProject()
    {
        var file = _projectPaths.ProjectSettingsFile;
        try
        {
            if (File.Exists(file)
                && JsonConvert.DeserializeObject<ProjectSettings>(
                    File.ReadAllText(file), ReplaceCollections) is { } loaded)
                return loaded;
        }
        catch (Exception)
        {
            PreserveCorruptFile(file);
        }

        return new ProjectSettings();
    }

    // Files written before the ReplaceCollections load fix accumulated duplicate categories (the defaults
    // plus the file's saved copy, restacked each launch). Collapse them by name — a category's identity in
    // the settings grid — so an existing install self-heals to a single set on its next load without
    // discarding the user's own category set.
    private static void HealCategories(EditorSettings settings) =>
        settings.EditorAssetSettings.Categories = settings.EditorAssetSettings.Categories
            .DistinctBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void PreserveCorruptFile(string file)
    {
        try
        {
            if (File.Exists(file))
                File.Move(file, file + ".corrupt", overwrite: true);
        }
        catch (Exception)
        {
            // Best-effort; if it can't be moved aside, the next Save() overwrites it anyway.
        }
    }
}
