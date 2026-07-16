using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Hosting;
using Toybox.Studio.Logging;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Projects;

/// <summary>
/// Switches the active project in-process (File ▸ Open ▸ Project) without restarting the editor. Almost
/// every project-scoped service re-hydrates itself off the engine connection lifecycle — the
/// <c>AssetCatalog</c> refreshes on connect and clears on disconnect, <c>SettingsManager</c> swaps its
/// <c>App</c> mirror as the new project's settings row appears, and every viewport stream stops and
/// restarts — so a switch is just: stop the engine session, repoint the <see cref="Project"/> singleton,
/// remember it, announce the change, then relaunch the engine against the new project. The rest reloads
/// itself. Serialized so a second switch can't race a build already in flight.
/// </summary>
public sealed class ProjectSwitcher(
    Project project,
    ProjectPaths projectPaths,
    ProjectLoader loader,
    EngineCoordinator coordinator,
    AppHost<Engine> host,
    SettingsManager settings,
    EventDispatcher events,
    Logger log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Whether a switch is currently running (the menu action disables while so).</summary>
    public bool IsSwitching { get; private set; }

    /// <summary>Whether the open project has unsaved settings changes a switch would discard.</summary>
    public bool HasUnsavedChanges => settings.App?.IsDirty is true;

    /// <summary>Saves the open project's settings if dirty — the "Save" answer to the unsaved-changes gate.</summary>
    public Task SaveChangesAsync() =>
        settings.App is { IsDirty: true } app ? app.SaveAsync() : Task.CompletedTask;

    /// <summary>Whether <paramref name="root"/> is a different project than the one already open.</summary>
    public bool IsDifferentProject(string root) =>
        !string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)),
            project.Path,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Switches the active project to the one at <paramref name="root"/> (a no-op when it's already open).
    /// The caller resolves any unsaved-changes gate first. Bounces the engine session and repoints the
    /// singletons; the connect/disconnect-driven services reload the new project's world, assets, and
    /// settings themselves.
    /// </summary>
    public async Task SwitchAsync(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (string.Equals(root, project.Path, StringComparison.OrdinalIgnoreCase))
            return;

        // Reject a second switch racing a build already in flight rather than tearing down mid-launch.
        if (!await _gate.WaitAsync(0).ContinueOnAnyContext())
        {
            log.Warning("A project switch is already in progress; ignoring the request.");
            return;
        }

        IsSwitching = true;
        try
        {
            log.Info($"Switching project to '{root}'…");

            // Tear the current engine session fully down first (teardown is order-sensitive; awaiting it
            // guarantees the process exits and the in-tree binaries unlock before the next build relinks).
            // The Disconnected it dispatches clears the catalog, drops the settings mirror, and stops the
            // viewports — all automatically. (Not RestartAsync, which would replay the OLD project's launch.)
            await host.StopAsync().ContinueOnAnyContext();

            // Repoint the active project (mutated in place — services hold this instance) and remember it.
            loader.Load(root, project);
            projectPaths.Root = project.Path;
            RecentProjects.Remember(settings.Editor.Projects, root);
            await settings.ApplyAsync().ContinueOnAnyContext();

            // Swap in the new project's project-scoped editor settings (build, gizmos, asset browser) from
            // its .toybox folder; the dispatched EditorSettingsChanged refreshes any consumer holding the old
            // project's values (e.g. the Asset Browser rail).
            settings.LoadProjectSettings();

            // Refresh name-bound UI (the Project data type has no change notification of its own).
            events.Dispatch(new ProjectChanged(project));

            // Relaunch the engine against the new project (compiles it first). Connect drives the catalog,
            // settings mirror, and viewports to re-hydrate. Mirror the startup skip when auto-launch is off.
            if (settings.Editor.Engine.AutoLaunchEngine)
                await coordinator.StartEngineAsync().ContinueOnAnyContext();
            else
                log.Info("Engine auto-launch is disabled; the new project's engine was not launched.");

            log.Info($"Switched project to '{project.Name}'.");
        }
        catch (Exception exception)
        {
            log.Error($"Project switch failed: {exception.Message}");
        }
        finally
        {
            IsSwitching = false;
            _gate.Release();
        }
    }
}
