using Toybox.Studio.Project.Assets;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Project;

/// <summary>
/// Owns the open project's settings asset (its AppSettings.json). Split out of the editor's
/// <see cref="Toybox.Studio.Settings.SettingsManager"/> because loading it depends on the asset system: the
/// asset is (re)loaded via <see cref="ReloadProjectAsync"/> whenever the open project changes or the engine
/// connects (the engine supplies the full settings schema). Raises <see cref="Changed"/> so the project
/// settings panel can rebuild without polling.
/// </summary>
public sealed class ProjectSettingsService : IListenable
{
    // Lazy to break a construction cycle (AssetServices → AssetCatalog → Session → …): the asset bundle is only
    // needed later — loading the project settings asset — never at construction.
    private readonly Lazy<AssetServices> _assets;

    public ProjectSettingsService(Lazy<AssetServices> assets) => _assets = assets;

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>The open project's settings asset (its AppSettings.json), or null when no project is open / it
    /// hasn't loaded yet. (Re)loaded by <see cref="ReloadProjectAsync"/>.</summary>
    public ProjectSettings? Project { get; private set; }

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

    /// <summary>Broadcasts that the project settings changed. Always raises <see cref="Changed"/> on the UI thread.</summary>
    public void NotifyChanged() => Dispatch.To(DispatchContext.UI, () => Changed?.Invoke());
}
