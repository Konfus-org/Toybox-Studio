using System.Collections.ObjectModel;
using System.Linq;
using Toybox.Studio;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Status;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Favorites;
using Toybox.Studio.Logging;
using Toybox.Studio.Project;
using Toybox.Studio.Worlds;
using Toybox.Studio.Shell.Panels;
using Toybox.Studio.Shell.Workspace;
using Toybox.Studio.Ecs;
using Toybox.Studio.WorldTree;

namespace Toybox.Studio.Shell;

/// <summary>
/// Composes the top-strip widgets into the application shell, hosts the menu actions, and owns the
/// <see cref="Workspace"/> — which manages every dockable panel and the dock layout. Individual panels are
/// no longer referenced here by name; they flow through the workspace's catalog.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Session _session;
    private readonly EngineWatcher _watcher;
    private readonly ProjectBuilder _builder;
    private readonly Logger _log;
    private readonly ProjectManager _projects;
    private readonly FilePicker _filePicker;
    private readonly CommandRunner _commandRunner;
    private readonly GameState _world;
    private readonly AssetCatalog _assets;
    private readonly AssetOpener _assetOpener;

    public ShellViewModel(
        StatusViewModel status,
        WorkspaceViewModel workspace,
        Session session,
        EngineWatcher watcher,
        ProjectBuilder builder,
        Logger log,
        ProjectManager projects,
        FilePicker filePicker,
        CommandRunner commandRunner,
        GameState world,
        AssetCatalog assets,
        AssetOpener assetOpener,
        FavoritesManager favorites)
    {
        Status = status;
        Workspace = workspace;

        _session = session;
        _watcher = watcher;
        _builder = builder;
        _log = log;
        _projects = projects;
        _filePicker = filePicker;
        _commandRunner = commandRunner;
        _world = world;
        _assets = assets;
        _assetOpener = assetOpener;
        _favorites = favorites;

        BuildMenuActions();
        // The engine-lifecycle actions enable/disable with the engine state (Start only when off; Stop/Restart
        // only when something's running). The watcher raises on the UI thread, so re-evaluate there.
        _watcher.StateChanged += OnEngineStateChanged;
        // Keep the generated Favorites menu in step with the star toggles (fires immediately for the first build).
        favorites.Listen(RefreshFavorites);

        projects.ProjectChanged += _ => Dispatch.To(DispatchContext.UI, RefreshTitle);
        // A project rename (its AppSettings "name" edited in Settings) updates the title without a relaunch.
        projects.ProjectRenamed += _ => Dispatch.To(DispatchContext.UI, RefreshTitle);
        RefreshTitle();
    }

    public StatusViewModel Status { get; }

    /// <summary>The window manager: registered dockables, live dock state, and open/reset/save actions.</summary>
    public WorkspaceViewModel Workspace { get; }

    // The favoritable menu-bar actions, by id, so the native menu items bind their icon/star and the generated
    // Favorites menu can re-list them.
    private readonly FavoritesManager _favorites;
    private readonly List<MenuActionViewModel> _actions = [];

    /// <summary>The starred menu-bar actions, surfaced in the generated "Favorites" menu (empty when none).</summary>
    public ObservableCollection<MenuActionViewModel> Favorites { get; } = [];

    public bool HasFavorites => Favorites.Count > 0;

    /// <summary>One favoritable action per registered dockable: opens (or focuses) the window by id. Drives the
    /// Windows menu, so each window carries an icon and a star — and a starred window joins the Favorites menu.</summary>
    public IReadOnlyList<MenuActionViewModel> WindowActions { get; private set; } = [];

    /// <summary>The favoritable menu-bar actions (icon + label + command + star). Each is captured directly from
    /// its registration in <see cref="BuildMenuActions"/>, so there is no id-string lookup to keep in sync.</summary>
    public MenuActionViewModel SaveAction { get; private set; } = null!;
    public MenuActionViewModel SaveAllAction { get; private set; } = null!;
    public MenuActionViewModel OpenProjectAction { get; private set; } = null!;
    public MenuActionViewModel OpenAssetAction { get; private set; } = null!;
    public MenuActionViewModel CompileAction { get; private set; } = null!;
    public MenuActionViewModel ShipDebugAction { get; private set; } = null!;
    public MenuActionViewModel ShipReleaseAction { get; private set; } = null!;
    public MenuActionViewModel StartEngineAction { get; private set; } = null!;
    public MenuActionViewModel RestartEngineAction { get; private set; } = null!;
    public MenuActionViewModel StopEngineAction { get; private set; } = null!;
    public MenuActionViewModel AttachAction { get; private set; } = null!;
    public MenuActionViewModel DebugEditorAction { get; private set; } = null!;
    public MenuActionViewModel ResetLayoutAction { get; private set; } = null!;
    public MenuActionViewModel SaveLayoutAction { get; private set; } = null!;
    public MenuActionViewModel LoadLayoutAction { get; private set; } = null!;

    private void BuildMenuActions()
    {
        MenuActionViewModel Add(string id, string label, Icon icon, Avalonia.Media.Color? color, System.Windows.Input.ICommand command, object? parameter = null)
        {
            var action = new MenuActionViewModel(id, label, icon, color, command, _favorites, parameter);
            _actions.Add(action);
            return action;
        }

        SaveAction = Add("save", "Save", Icon.Save, Colors.Blue, SaveCommand);
        SaveAllAction = Add("saveAll", "Save All", Icon.SaveAll, Colors.Blue, SaveAllCommand);
        OpenProjectAction = Add("openProject", "Project…", Icon.FolderOpen, Colors.Yellow, OpenProjectCommand);
        OpenAssetAction = Add("openAsset", "Asset…", Icon.FileInput, Colors.Yellow, OpenAssetCommand);
        CompileAction = Add("compile", "Compile", Icon.Hammer, Colors.Green, CompileCommand);
        ShipDebugAction = Add("shipDebug", "Ship Debug", Icon.Bug, Colors.Green, ShipCommand, "Debug");
        ShipReleaseAction = Add("shipRelease", "Ship Release", Icon.Package, Colors.Green, ShipCommand, "Release");
        StartEngineAction = Add("startEngine", "Start", Icon.Power, Colors.Green, StartEngineCommand);
        RestartEngineAction = Add("restartEngine", "Restart", Icon.RotateCcw, Colors.Yellow, RestartEngineCommand);
        StopEngineAction = Add("stopEngine", "Stop", Icon.PowerOff, Colors.Red, StopEngineCommand);
        AttachAction = Add("attach", "Attach to Running Instance", Icon.Plug, Colors.Cyan, AttachCommand);
        DebugEditorAction = Add("debugEditor", "Avalonia Dev Tools", Icon.Wrench, Colors.Grey, DebugEditorCommand);
        ResetLayoutAction = Add("resetLayout", "Reset Layout", Icon.LayoutDashboard, Colors.Magenta, Workspace.ResetLayoutCommand);
        SaveLayoutAction = Add("saveLayout", "Save Layout", Icon.LayoutDashboard, Colors.Magenta, Workspace.SaveLayoutCommand);
        LoadLayoutAction = Add("loadLayout", "Load Layout", Icon.LayoutDashboard, Colors.Magenta, Workspace.LoadLayoutCommand);

        // One favoritable action per dockable, opened (or focused) by passing the descriptor itself. Added to
        // _actions so they share the Favorites machinery (star indicator + the generated Favorites menu) with the
        // fixed menu-bar actions; the descriptor's key is the stable favorites id.
        WindowActions = [.. Workspace.All.Select(descriptor => Add(
            descriptor.Key,
            descriptor.Title,
            descriptor.Icon == Icon.None ? Icon.AppWindow : descriptor.Icon,
            descriptor.IconColor,
            Workspace.OpenDockableCommand,
            descriptor))];
    }

    // Rebuilds the Favorites menu from the starred actions and refreshes each action's star indicator.
    private void RefreshFavorites()
    {
        foreach (var action in _actions)
            action.RefreshFavorite();

        Favorites.Clear();
        foreach (var action in _actions.Where(action => action.IsFavorite))
            Favorites.Add(action);
        OnPropertyChanged(nameof(HasFavorites));
    }

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

        if (_world.Active.IsDirty)
            unsaved.Add(("World", () => _world.Active.SaveAsync()));

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
        _world.Active.IsDirty ? _world.Active.SaveAsync() : Task.CompletedTask;

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
    /// Opens an asset chosen from the in-app picker, routing by type through the shared <see cref="AssetOpener"/>
    /// (scripts/shaders → code editor, worlds → active world, models/materials/textures → Asset Viewer,
    /// anything else → the OS default program).
    /// </summary>
    [RelayCommand]
    private async Task OpenAssetAsync()
    {
        // Resume on the UI thread after the dialog: the routing opens dockables, which touch the DockControl
        // and must run on the UI thread.
        var pick = await AssetPicker.ShowAsync("Open Asset", _assets.Assets, 0).ContinueOnSameContext();
        if (!pick.Confirmed || pick.Id == 0)
            return;

        if (_assets.Resolve(pick.Id) is not { } asset)
        {
            _log.Error("The chosen asset could not be resolved.");
            return;
        }

        await _assetOpener.OpenAsync(asset).ContinueOnSameContext();
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

    /// <summary>Compiles and launches the engine for the current project. Enabled only while it's fully off.</summary>
    [RelayCommand(CanExecute = nameof(CanStartEngine))]
    private Task StartEngineAsync() => _session.StartAsync();

    private bool CanStartEngine => _watcher.State == EngineState.Off;

    /// <summary>Tears down and relaunches the engine. Enabled only while something is running.</summary>
    [RelayCommand(CanExecute = nameof(CanControlEngine))]
    private Task RestartEngineAsync() => _session.RestartAsync();

    /// <summary>Stops the running engine (an owned process exits; an attached one is detached). Enabled only
    /// while something is running.</summary>
    [RelayCommand(CanExecute = nameof(CanControlEngine))]
    private Task StopEngineAsync() => _session.StopAsync();

    private bool CanControlEngine => _watcher.State != EngineState.Off;

    // Re-evaluate the engine-lifecycle actions' enabled state whenever the engine state changes.
    private void OnEngineStateChanged(EngineState state)
    {
        StartEngineCommand.NotifyCanExecuteChanged();
        RestartEngineCommand.NotifyCanExecuteChanged();
        StopEngineCommand.NotifyCanExecuteChanged();
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
