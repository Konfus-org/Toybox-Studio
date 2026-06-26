using System.IO;
using Toybox.Studio.Services;
using Toybox.Studio.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Widgets.Status;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.Project;
using Toybox.Studio.Services.Scripting;
using Toybox.Studio.Services.World;
using Toybox.Studio.Shell.Panels;
using Toybox.Studio.Services.AssetViewing;
using Toybox.Studio.Shell.Workspace;
using Toybox.Studio.Widgets.Ecs;

namespace Toybox.Studio.Shell;

/// <summary>
/// Composes the top-strip widgets into the application shell, hosts the menu actions, and owns the
/// <see cref="Workspace"/> — which manages every dockable panel and the dock layout. Individual panels are
/// no longer referenced here by name; they flow through the workspace's catalog.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Session _session;
    private readonly ProjectBuilder _builder;
    private readonly Logger _log;
    private readonly ProjectManager _projects;
    private readonly FilePicker _filePicker;
    private readonly CommandRunner _commandRunner;
    private readonly WorldManager _world;
    private readonly AssetCatalog _assets;
    private readonly ScriptEditorLauncher _scriptEditor;
    private readonly AssetViewerLauncher _assetViewer;

    public ShellViewModel(
        StatusViewModel status,
        WorkspaceViewModel workspace,
        Session session,
        ProjectBuilder builder,
        Logger log,
        ProjectManager projects,
        FilePicker filePicker,
        CommandRunner commandRunner,
        WorldManager world,
        AssetCatalog assets,
        ScriptEditorLauncher scriptEditor,
        AssetViewerLauncher assetViewer)
    {
        Status = status;
        Workspace = workspace;

        _session = session;
        _builder = builder;
        _log = log;
        _projects = projects;
        _filePicker = filePicker;
        _commandRunner = commandRunner;
        _world = world;
        _assets = assets;
        _scriptEditor = scriptEditor;
        _assetViewer = assetViewer;

        projects.ProjectChanged += _ => Dispatch.To(DispatchContext.UI, RefreshTitle);
        // A project rename (its AppSettings "name" edited in Settings) updates the title without a relaunch.
        projects.ProjectRenamed += _ => Dispatch.To(DispatchContext.UI, RefreshTitle);
        RefreshTitle();
    }

    public StatusViewModel Status { get; }

    /// <summary>The window manager: registered dockables, live dock state, and open/reset/save actions.</summary>
    public WorkspaceViewModel Workspace { get; }

    [ObservableProperty]
    public partial string Title { get; private set; } = "Toybox Studio";

    /// <summary>Toolbar quick-search text. Reserved for a panel/command quick-open; bound by the toolbar field.</summary>
    [ObservableProperty]
    public partial string ToolbarSearch { get; set; } = "";

    /// <summary>Saves whatever is focused: a focused buffered panel (e.g. Settings) commits itself; the
    /// viewport (a live panel), world tree, or inspector saves the world. A no-op when nothing's dirty.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        switch (Workspace.FocusedDockable())
        {
            case DataPanel { IsLive: false, IsDirty: false }:
                break; // Clean buffered panel — nothing to save.
            case DataPanel panel:
                await panel.SaveAsync().ContinueOnSameContext(); // Dirty buffered panel, or the live viewport.
                break;
            case WorldViewModel:
                await SaveWorldAsync().ContinueOnSameContext(); // World tree / inspector.
                break;
        }
    }

    /// <summary>Saves every open panel plus the world (each only when it has unsaved changes).</summary>
    [RelayCommand]
    private async Task SaveAllAsync()
    {
        foreach (var panel in Workspace.OpenPanels())
        {
            if (panel.HasUnsavedChanges)
                await panel.SaveAsync().ContinueOnSameContext();
        }

        await SaveWorldAsync().ContinueOnSameContext();
    }

    /// <summary>
    /// The consolidated unsaved-changes gate for app close: gathers every buffered panel with unsaved edits
    /// plus the world, shows ONE Save All / Discard All / Cancel prompt, and returns whether the app may
    /// close. Save All saves each item; Cancel keeps the app open.
    /// </summary>
    public async Task<bool> RequestCloseAsync()
    {
        var unsaved = new List<(string Name, Func<Task> Save)>();
        foreach (var panel in Workspace.OpenPanels())
        {
            if (panel.HasUnsavedChanges)
                unsaved.Add((panel.BaseTitle, panel.SaveAsync));
        }

        if (_world.IsDirty)
            unsaved.Add(("World", () => _world.SaveAsync()));

        if (unsaved.Count == 0)
            return true;

        var choice = await Popups
            .ShowSaveChangesAsync([.. unsaved.Select(item => item.Name)])
            .ContinueOnSameContext();
        if (choice == SaveChoice.Cancel)
            return false;

        if (choice == SaveChoice.Save)
        {
            foreach (var item in unsaved)
                await item.Save().ContinueOnSameContext();
        }

        return true;
    }

    private Task SaveWorldAsync() =>
        _world.IsDirty ? _world.SaveAsync() : Task.CompletedTask;

    // TODO: Make a toolbar VM and widget for these and move the commands there.
    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var path = await _filePicker.PickFolderAsync("Open Toybox Project").ContinueOnAnyContext();
        if (path is null)
            return;

        if (!_projects.TryOpen(path, out var error))
            _log.Error(error ?? "Failed to open the project.");
    }

    /// <summary>
    /// Opens an asset chosen from the in-app picker, routing by type: scripts and shaders open in the
    /// Monaco script editor; worlds/chunks switch the active editing world; textures, models and
    /// materials open in a new Asset Viewer (an isolated orbit preview).
    /// </summary>
    [RelayCommand]
    private async Task OpenAssetAsync()
    {
        // Resume on the UI thread after the dialog: the routing below opens dockables (script editor /
        // asset viewer), which touch the DockControl and must run on the UI thread.
        var pick = await AssetPicker.ShowAsync("Open Asset", _assets.Assets, 0).ContinueOnSameContext();
        if (!pick.Confirmed || pick.Id == 0)
            return;

        if (_assets.Resolve(pick.Id) is not { } asset)
        {
            _log.Error("The chosen asset could not be resolved.");
            return;
        }

        if (asset.IsScript || IsShader(asset.Type))
        {
            if (_projects.CurrentProject is { } project)
                _scriptEditor.Open(ResolveAssetPath(project, asset.Path));
            else
                _log.Error("Open a project before opening a script.");
            return;
        }

        if (asset.Type is "world" or "chunk")
        {
            await _world.OpenWorldAsync(asset.Id).ContinueOnAnyContext();
            return;
        }

        _assetViewer.Open(asset);
    }

    // Shader source extensions open in the text editor alongside scripts.
    private static bool IsShader(string type) =>
        type is "glsl" or "hlsl" or "vert" or "frag" or "vertex" or "fragment" or "geometry"
            or "compute" or "comp";

    // Resolves a project-relative asset path to an absolute one (under the project root, else its
    // Assets folder) so the script editor can open the file. Mirrors App.ResolveAssetPath.
    private static string ResolveAssetPath(ProjectInfo project, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return project.AssetsDirectory;
        if (Path.IsPathRooted(relativePath))
            return relativePath;

        var underRoot = Path.Combine(project.RootDirectory, relativePath);
        if (File.Exists(underRoot))
            return underRoot;

        var underAssets = Path.Combine(project.AssetsDirectory, relativePath);
        return File.Exists(underAssets) ? underAssets : underRoot;
    }

    [RelayCommand]
    private Task CompileAsync()
    {
        return _builder.BuildAsync(CancellationToken.None);
    }

    [RelayCommand]
    private Task AttachAsync()
    {
        return _session.AttachAsync(InstanceDetector.DefaultEnginePort);
    }

    [RelayCommand]
    private async Task ShipAsync(string configuration)
    {
        var folder = await _filePicker.PickFolderAsync($"Choose {configuration} Ship Output Folder")
            .ContinueOnAnyContext();
        if (folder is null)
            return;

        await _builder.ShipAsync(configuration, folder, CancellationToken.None).ContinueOnAnyContext();
    }

    [RelayCommand]
    private async Task DebugEditor()
    {
        var result = await _commandRunner.RunAsync("avdt").ContinueOnAnyContext();
        if (!result)
        {
            _log.Error("Failed to launch the editor debug tool. Make sure avdt is on your PATH.");
            await Popups.ShowErrorAsync(
                    "Failed To Launch Dev Tools",
                    "Failed to launch Avalonia Developer Tools. Please ensure you have them installed via: dotnet tool install --global AvaloniaUI.DeveloperTools").ContinueOnAnyContext();
        }
    }

    private void RefreshTitle()
    {
        var project = _projects.CurrentProject;
        Title = project is null ? "Toybox Studio" : $"Toybox Studio — {project.Name}";
    }
}
