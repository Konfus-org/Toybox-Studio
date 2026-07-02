using Toybox.Studio.Project;
using Toybox.Studio.Project.Assets;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Settings;

/// <summary>
/// The single owner of the editor's persisted settings AND the open project's settings. The editor's own
/// <see cref="EditorSettings"/> load once at startup and live for the app's lifetime; the project's
/// <see cref="ProjectSettings"/> asset (its AppSettings.json) is (re)loaded via <see cref="ReloadProjectAsync"/>
/// whenever the open project changes or the engine connects (the engine supplies the full settings schema). Every
/// consumer reads through here rather than holding its own copy. Persisting goes through <see cref="SaveAsync"/>
/// (editor) / <see cref="SaveProjectAsync"/> (project), or <see cref="NotifyChanged"/> for an in-place edit — each
/// raises <see cref="Changed"/> so any area can react without polling (see <see cref="IListenable"/> and
/// <see cref="ListenableExtensions.Listen"/>).
/// </summary>
public sealed class SettingsManager : IListenable
{
    // Lazy to break a construction cycle (AssetServices → AssetCatalog → Session → SettingsManager): the asset
    // bundle is only needed later — loading the project settings asset — never at construction.
    private readonly Lazy<AssetServices> _assets;

    public SettingsManager(Lazy<AssetServices> assets) => _assets = assets;

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>The live editor settings instance. Stable for the app's lifetime; mutate then persist via this manager.</summary>
    public EditorSettings Settings { get; } = EditorSettings.Load();

    /// <summary>The open project's settings asset (its AppSettings.json), or null when no project is open / it
    /// hasn't loaded yet. (Re)loaded by <see cref="ReloadProjectAsync"/>.</summary>
    public ProjectSettings? Project { get; private set; }

    /// <summary>
    /// Writes the current editor settings to disk (off the UI thread) and then notifies listeners. Callers either
    /// <c>await</c> this (the Settings panel) or fire-and-forget it (theme pick, engine path, recent-project list).
    /// </summary>
    public async Task SaveAsync()
    {
        await Settings.SaveAsync().ContinueOnAnyContext();
        NotifyChanged();
    }

    /// <summary>
    /// (Re)loads the open project's settings asset from disk (enriched with the engine schema when connected) and
    /// notifies listeners. Wired to project-change and engine-connect at startup; null when no project is open.
    /// </summary>
    public async Task ReloadProjectAsync()
    {
        if (_assets.Value.Projects.CurrentProject is not { } project)
        {
            Project = null;
            NotifyChanged();
            return;
        }

        var info = new AssetMeta(0, "AppSettings", "appsettings", project.AppSettingsPath, HasMeta: false);
        var result = await new ProjectSettings(_assets.Value, info).LoadAsync().ContinueOnAnyContext();
        Project = result is { Success: true, Value: ProjectSettings settings } ? settings : null;
        NotifyChanged();
    }

    /// <summary>Persists the open project's settings (lean) through the project settings asset. A no-op when no
    /// project is loaded.</summary>
    public Task<Result> SaveProjectAsync(CancellationToken ct = default) =>
        Project is { } project ? project.SaveAsync(ct) : Task.FromResult(Result.Ok());

    /// <summary>
    /// Broadcasts that the settings changed without writing to disk — for re-applying the already-saved values
    /// after an in-place edit or a cancelled buffer revert. Always raises <see cref="Changed"/> on the UI thread
    /// (marshalling if called from a background thread, e.g. after a disk save), since listeners mutate Avalonia
    /// resources and view-model state directly.
    /// </summary>
    public void NotifyChanged() => Dispatch.To(DispatchContext.UI, () => Changed?.Invoke());
}
