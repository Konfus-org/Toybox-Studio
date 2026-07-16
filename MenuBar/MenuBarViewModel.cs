using Toybox.Studio.EngineApi.Types.Assets;
using System.Globalization;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Hosting;
using Toybox.Studio.AssetViewer;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Toybox.Studio.Coding;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Favorites;
using Toybox.Studio.Keybindings;
using Toybox.Studio.Logging;
using Toybox.Studio.Projects;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Composition;
using Toybox.Studio.Docking;
using Icon = IconPacks.Avalonia.Lucide.PackIconLucideKind;

namespace Toybox.Studio.MenuBar;

/// <summary>
/// The main menu's state and commands. Every item is an <see cref="EditorAction"/>: the constructor
/// registers the menu's actions (settings/keybindings, builds, attach, one per dockable, the layout
/// trio) with their default chords, each item's command publishes the action's
/// <see cref="EditorActionInvoked"/>, and this view-model executes them in its handler — so a menu
/// click and the action's keybinding run through exactly the same path, and every item is rebindable
/// in the Keybindings page. Shortcut hints come from the <see cref="EditorKeymap"/> and refresh as
/// bindings change. Build commands disable while any compile runs, tracked through the dispatched
/// <see cref="BuildStateChanged"/>.
/// </summary>
public sealed partial class MenuBarViewModel :
    ObservableEventSubscriber,
    IEventHandler<BuildStateChanged>,
    IEventHandler<EditorActionInvoked>,
    IEventHandler<KeybindingsChanged>,
    IEventHandler<FavoritesChanged>,
    IEventHandler<ProjectChanged>
{
    private readonly Project _project;
    private readonly ProjectBuilder _builder;
    private readonly EngineBuilder _engineBuilder;
    private readonly AppHost<Engine> _host;
    private readonly WorkspaceViewModel _workspace;
    private readonly ActionRegistry _actions;
    private readonly EditorKeymap _keymap;
    private readonly Logger _log;
    private readonly Popups _popups;
    private readonly AssetCatalog _catalog;
    private readonly AssetOpener _assetOpener;
    private readonly CoderLauncher _coder;
    private readonly ProjectSwitcher _projectSwitcher;
    private readonly FavoritesManager _favorites;
    private readonly ViewModelFactory _viewModels;
    private readonly Dictionary<string, DockableDescriptor> _windowActions = [];

    // Every favoritable bar action by id — the fixed File/Edit/Build/Debug items plus the data-driven
    // Window and Layout rows — so a keymap change can refresh their shortcut hints in place and the
    // Favorites menu can resolve a starred id back to the one row that runs it.
    private readonly Dictionary<string, MenuActionViewModel> _actionsById = [];

    private bool _isBuilding;

    public MenuBarViewModel(
        Project project,
        ProjectBuilder builder,
        EngineBuilder engineBuilder,
        AppHost<Engine> host,
        WorkspaceViewModel workspace,
        ActionRegistry actions,
        EditorKeymap keymap,
        Logger log,
        Popups popups,
        AssetCatalog catalog,
        AssetOpener assetOpener,
        CoderLauncher coder,
        ProjectSwitcher projectSwitcher,
        FavoritesManager favorites,
        EventDispatcher events,
        ViewModelFactory viewModels)
        : base(events)
    {
        _project = project;
        _builder = builder;
        _engineBuilder = engineBuilder;
        _host = host;
        _workspace = workspace;
        _actions = actions;
        _keymap = keymap;
        _log = log;
        _popups = popups;
        _catalog = catalog;
        _assetOpener = assetOpener;
        _coder = coder;
        _projectSwitcher = projectSwitcher;
        _favorites = favorites;
        _viewModels = viewModels;

        RegisterActions();
        CreateFixedItems();
        WindowItems = BuildWindowItems();
        LayoutItems = BuildLayoutItems();
        EntityItems = BuildEntityItems();
        RebuildFavorites();

        // The focused document drives Undo/Redo/Save's enabled state; refresh the commands when focus
        // moves or that document's history/dirty flag changes.
        _workspace.FocusedUndoStateChanged += OnFocusedUndoStateChanged;
        _workspace.FocusedSaveStateChanged += OnFocusedSaveStateChanged;
    }

    /// <summary>The fixed File/Edit/Build/Debug items, one favoritable row each (their inline menu items
    /// bind these for the star and shortcut hint; the same rows render in the Favorites menu).</summary>
    public MenuActionViewModel OpenProjectItem { get; private set; } = null!;

    public MenuActionViewModel OpenAssetItem { get; private set; } = null!;

    public MenuActionViewModel OpenSourceItem { get; private set; } = null!;

    public MenuActionViewModel SaveItem { get; private set; } = null!;

    public MenuActionViewModel UndoItem { get; private set; } = null!;

    public MenuActionViewModel RedoItem { get; private set; } = null!;

    public MenuActionViewModel SettingsItem { get; private set; } = null!;

    public MenuActionViewModel BuildDebugItem { get; private set; } = null!;

    public MenuActionViewModel BuildReleaseItem { get; private set; } = null!;

    public MenuActionViewModel BuildEngineItem { get; private set; } = null!;

    public MenuActionViewModel AttachItem { get; private set; } = null!;

    /// <summary>The starred actions, pinned under the top-level Favorites menu (in star order).</summary>
    public ObservableCollection<MenuActionViewModel> Favorites { get; } = [];

    public bool HasFavorites => Favorites.Count > 0;

    /// <summary>The Window menu's rows: every listed dockable, favoritable.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MenuActionViewModel> WindowItems { get; private set; }

    /// <summary>The Layout menu's rows: save / load / reset the dock arrangement, favoritable.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MenuActionViewModel> LayoutItems { get; private set; }

    /// <summary>Edit ▸ Entity's rows: the entity edit verbs (copy/cut/paste/duplicate/rename/delete),
    /// favoritable. They publish their action, which the focused Hierarchy panel executes.</summary>
    public IReadOnlyList<MenuActionViewModel> EntityItems { get; private set; } = [];

    /// <summary>The open project's name, heading its Build submenu. Read once the window binds — the
    /// launch flow loads the project long before the menu exists.</summary>
    public string AppName => _project.Name.Length > 0 ? _project.Name : "App";

    public void Handle(in BuildStateChanged evt)
    {
        var isBuilding = evt.IsBuilding;
        Dispatch.To(DispatchContext.UI, () =>
        {
            _isBuilding = isBuilding;
            BuildAppDebugCommand.NotifyCanExecuteChanged();
            BuildAppReleaseCommand.NotifyCanExecuteChanged();
            BuildEngineCommand.NotifyCanExecuteChanged();
        });
    }

    /// <summary>Executes the menu's actions — however they were invoked (a click published the same
    /// event a matched keybinding does). Ids other features own are simply not this handler's.</summary>
    public void Handle(in EditorActionInvoked evt)
    {
        var id = evt.ActionId;
        Dispatch.To(DispatchContext.UI, () => Execute(id));
    }

    // A binding changed: re-read every row's shortcut hint in place (the rows themselves are stable).
    public void Handle(in KeybindingsChanged evt) => Dispatch.To(DispatchContext.UI, () =>
    {
        foreach (var action in _actionsById.Values)
            action.Gesture = _keymap.GestureFor(action.Id);
    });

    // The favorites store changed: refresh every row's star and re-pin the Favorites menu. Only the menu
    // bar's own host matters here; changes to a context menu's favorites are its own business.
    public void Handle(in FavoritesChanged evt)
    {
        if (evt.Host != FavoritesManager.MenuBarHost)
            return;

        Dispatch.To(DispatchContext.UI, () =>
        {
            foreach (var action in _actionsById.Values)
                action.RefreshFavorite();
            RebuildFavorites();
        });
    }

    // The active project switched (in-process): the Build submenu header binds the project's name.
    public void Handle(in ProjectChanged evt) =>
        Dispatch.To(DispatchContext.UI, () => OnPropertyChanged(nameof(AppName)));

    // The focused document changed, or its history did: re-evaluate whether Undo/Redo can run.
    private void OnFocusedUndoStateChanged() => Dispatch.To(DispatchContext.UI, () =>
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    });

    // The focused document changed, or its dirty flag flipped: re-evaluate whether Save can run.
    private void OnFocusedSaveStateChanged() =>
        Dispatch.To(DispatchContext.UI, SaveCommand.NotifyCanExecuteChanged);

    private bool CanBuild => !_isBuilding;

    // Undo/Redo/Save enable off the focused document (see WorkspaceViewModel.CanUndoFocused/CanSaveFocused).
    private bool CanUndo => _workspace.CanUndoFocused;

    private bool CanRedo => _workspace.CanRedoFocused;

    private bool CanSave => _workspace.CanSaveFocused;

    // The menu items publish their action; execution lives in the Handle above.
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => _actions.Invoke(ActionIds.Save);

    [RelayCommand]
    private void OpenProject() => _actions.Invoke(ActionIds.OpenProject);

    [RelayCommand]
    private void OpenAsset() => _actions.Invoke(ActionIds.OpenAsset);

    [RelayCommand]
    private void OpenSource() => _actions.Invoke(ActionIds.OpenSource);

    [RelayCommand]
    private void OpenSettings() => _actions.Invoke(ActionIds.OpenSettings);

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => _actions.Invoke(ActionIds.Undo);

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => _actions.Invoke(ActionIds.Redo);

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private void BuildAppDebug() => _actions.Invoke(ActionIds.BuildAppDebug);

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private void BuildAppRelease() => _actions.Invoke(ActionIds.BuildAppRelease);

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private void BuildEngine() => _actions.Invoke(ActionIds.BuildEngine);

    [RelayCommand]
    private void AttachToRunningBuild() => _actions.Invoke(ActionIds.AttachToRunningBuild);

    private void Execute(string id)
    {
        if (_windowActions.TryGetValue(id, out var descriptor))
        {
            _workspace.Open(descriptor);
            return;
        }

        switch (id)
        {
            // The keyboard path skips the command's CanExecute; SaveFocusedAsync no-ops when the focused
            // document (if any) has nothing to save.
            case ActionIds.Save:
                _workspace.SaveFocusedAsync().FireAndForget();
                break;
            case ActionIds.OpenProject:
                OpenProjectAsync().FireAndForget();
                break;
            case ActionIds.OpenAsset:
                OpenAssetAsync().FireAndForget();
                break;
            case ActionIds.OpenSource:
                OpenSourceAsync().FireAndForget();
                break;
            case ActionIds.OpenSettings:
                _workspace.Open<SettingsViewModel>();
                break;
            // The keyboard path skips the commands' CanExecute; UndoFocused itself no-ops when the
            // focused document (if any) has nothing to undo.
            case ActionIds.Undo:
                _workspace.UndoFocused();
                break;
            case ActionIds.Redo:
                _workspace.RedoFocused();
                break;
            // The keyboard path skips the commands' CanExecute, so the build gate re-applies here.
            case ActionIds.BuildAppDebug when CanBuild:
                BuildAppAsync(BuildMode.Debug).FireAndForget();
                break;
            case ActionIds.BuildAppRelease when CanBuild:
                BuildAppAsync(BuildMode.Release).FireAndForget();
                break;
            case ActionIds.BuildEngine when CanBuild:
                BuildEngineAsync().FireAndForget();
                break;
            case ActionIds.AttachToRunningBuild:
                AttachAsync().FireAndForget();
                break;
            case ActionIds.SaveLayout:
                _workspace.SaveLayoutCommand.Execute(null);
                break;
            case ActionIds.LoadLayout:
                _workspace.LoadLayoutCommand.Execute(null);
                break;
            case ActionIds.ResetLayout:
                _workspace.ResetLayoutCommand.Execute(null);
                break;
        }
    }

    private void RegisterActions()
    {
        _actions.Register(new EditorAction
        {
            Id = ActionIds.Save,
            Title = "Save",
            Category = "File",
            Icon = Icon.Save,
            DefaultChords = [Chord(InputKey.S, ctrl: true)],
        });
        _actions.Register(new EditorAction
        {
            Id = ActionIds.OpenProject,
            Title = "Open Project…",
            Category = "File",
            Icon = Icon.FolderOpen,
            DefaultChords = [Chord(InputKey.O, ctrl: true, shift: true)],
        });
        _actions.Register(new EditorAction
        {
            Id = ActionIds.OpenAsset,
            Title = "Open Asset…",
            Category = "File",
            Icon = Icon.Image,
            DefaultChords = [Chord(InputKey.O, ctrl: true)],
        });
        _actions.Register(new EditorAction
        {
            Id = ActionIds.OpenSource,
            Title = "Open Source…",
            Category = "File",
            Icon = Icon.FileCode,
        });
        _actions.Register(new EditorAction
        {
            Id = ActionIds.OpenSettings,
            Title = "Open Settings",
            Category = "Edit",
            Icon = Icon.Settings,
            DefaultChords = [Chord(InputKey.Comma, ctrl: true)],
        });
        _actions.Register(new EditorAction
        {
            Id = ActionIds.Undo,
            Title = "Undo",
            Category = "Edit",
            Icon = Icon.Undo2,
            DefaultChords = [Chord(InputKey.Z, ctrl: true)],
        });
        _actions.Register(new EditorAction
        {
            Id = ActionIds.Redo,
            Title = "Redo",
            Category = "Edit",
            Icon = Icon.Redo2,
            DefaultChords = [Chord(InputKey.Z, ctrl: true, shift: true), Chord(InputKey.Y, ctrl: true)],
        });
        _actions.Register(new EditorAction
        {
            Id = ActionIds.BuildAppDebug,
            Title = "Build App (Debug)",
            Category = "Build",
            Icon = Icon.Bug,
            DefaultChords = [Chord(InputKey.B, ctrl: true)],
        });
        _actions.Register(new EditorAction
        {
            Id = ActionIds.BuildAppRelease,
            Title = "Build App (Release)",
            Category = "Build",
            Icon = Icon.Package,
            DefaultChords = [Chord(InputKey.B, ctrl: true, shift: true)],
        });
        _actions.Register(new EditorAction
        {
            Id = ActionIds.BuildEngine,
            Title = "Build Engine",
            Category = "Build",
            Icon = Icon.Cog,
        });
        _actions.Register(new EditorAction
        {
            Id = ActionIds.AttachToRunningBuild,
            Title = "Attach To Running Build",
            Category = "Debug",
            Icon = Icon.Plug,
        });
        _actions.Register(new EditorAction
        {
            Id = ActionIds.SaveLayout,
            Title = "Save Layout",
            Category = "Layout",
            Icon = Icon.Save,
        });
        _actions.Register(new EditorAction
        {
            Id = ActionIds.LoadLayout,
            Title = "Load Layout",
            Category = "Layout",
            Icon = Icon.FolderOpen,
        });
        _actions.Register(new EditorAction
        {
            Id = ActionIds.ResetLayout,
            Title = "Reset Layout",
            Category = "Layout",
            Icon = Icon.RotateCcw,
        });

        // One open action per registered dockable (Settings opts out via ShowInWindowMenu — it lives
        // under Edit). The registry is fixed after composition, so this runs once.
        foreach (var descriptor in _workspace.All.Where(candidate => candidate.ShowInWindowMenu))
        {
            var id = ActionIds.OpenWindow(descriptor.Key);
            _windowActions[id] = descriptor;
            _actions.Register(new EditorAction
            {
                Id = id,
                Title = $"Open {descriptor.Title}",
                Category = "Window",
                Icon = descriptor.Icon,
            });
        }
    }

    // The fixed File/Edit/Build/Debug rows, built once. They reuse this view-model's generated commands
    // (so the build gate and focused-document enabled state still apply) and are keyed by their action id
    // for the star. Each is tracked so a keybinding change refreshes its hint and it can appear in the
    // Favorites menu.
    private void CreateFixedItems()
    {
        OpenProjectItem = FixedItem(ActionIds.OpenProject, "Project…", Icon.Boxes, OpenProjectCommand);
        OpenAssetItem = FixedItem(ActionIds.OpenAsset, "Asset…", Icon.Image, OpenAssetCommand);
        OpenSourceItem = FixedItem(ActionIds.OpenSource, "Source…", Icon.FileCode, OpenSourceCommand);
        SaveItem = FixedItem(ActionIds.Save, "Save", Icon.Save, SaveCommand);
        UndoItem = FixedItem(ActionIds.Undo, "Undo", Icon.Undo2, UndoCommand);
        RedoItem = FixedItem(ActionIds.Redo, "Redo", Icon.Redo2, RedoCommand);
        SettingsItem = FixedItem(ActionIds.OpenSettings, "Settings", Icon.Settings, OpenSettingsCommand);
        BuildDebugItem = FixedItem(ActionIds.BuildAppDebug, "Debug", Icon.Bug, BuildAppDebugCommand);
        BuildReleaseItem = FixedItem(ActionIds.BuildAppRelease, "Release", Icon.Package, BuildAppReleaseCommand);
        BuildEngineItem = FixedItem(ActionIds.BuildEngine, "Engine", Icon.Cog, BuildEngineCommand);
        AttachItem = FixedItem(
            ActionIds.AttachToRunningBuild, "Attach To Running Build", Icon.Plug, AttachToRunningBuildCommand);
    }

    private MenuActionViewModel FixedItem(string id, string title, Icon icon, ICommand command) =>
        Track(new MenuActionViewModel(id, title, icon, command, _favorites, _keymap.GestureFor(id)));

    private IReadOnlyList<MenuActionViewModel> BuildWindowItems() =>
        [.. _workspace.All
            .Where(descriptor => descriptor.ShowInWindowMenu)
            .Select(descriptor =>
            {
                var id = ActionIds.OpenWindow(descriptor.Key);
                return Track(new MenuActionViewModel(
                    id, descriptor.Title, descriptor.Icon,
                    new RelayCommand(() => _actions.Invoke(id)), _favorites, _keymap.GestureFor(id)));
            })];

    private IReadOnlyList<MenuActionViewModel> BuildLayoutItems() =>
    [
        LayoutItem(ActionIds.SaveLayout, "Save Layout…", Icon.Save),
        LayoutItem(ActionIds.LoadLayout, "Load Layout…", Icon.FolderOpen),
        LayoutItem(ActionIds.ResetLayout, "Reset Layout", Icon.RotateCcw),
    ];

    private MenuActionViewModel LayoutItem(string id, string title, Icon icon) =>
        Track(new MenuActionViewModel(
            id, title, icon, new RelayCommand(() => _actions.Invoke(id)), _favorites, _keymap.GestureFor(id)));

    // Edit ▸ Entity's rows. Each publishes its action (registered with its chord by WorldTree's
    // WorldTreeActions); the focused Hierarchy panel executes it against the current selection.
    private IReadOnlyList<MenuActionViewModel> BuildEntityItems() =>
    [
        EntityItem(ActionIds.EntityCopy, "Copy", Icon.Copy),
        EntityItem(ActionIds.EntityCut, "Cut", Icon.Scissors),
        EntityItem(ActionIds.EntityPaste, "Paste", Icon.ClipboardPaste),
        EntityItem(ActionIds.EntityDuplicate, "Duplicate", Icon.CopyPlus),
        EntityItem(ActionIds.EntityRename, "Rename", Icon.Pencil),
        EntityItem(ActionIds.EntityDelete, "Delete", Icon.Trash2),
    ];

    private MenuActionViewModel EntityItem(string id, string title, Icon icon) =>
        Track(new MenuActionViewModel(
            id, title, icon, new RelayCommand(() => _actions.Invoke(id)), _favorites, _keymap.GestureFor(id)));

    // Records a row against its id so keybinding refreshes and the Favorites menu can find it.
    private MenuActionViewModel Track(MenuActionViewModel action)
    {
        _actionsById[action.Id] = action;
        return action;
    }

    // Re-pins the Favorites menu from the stored order, mapping each starred id back to its one row.
    private void RebuildFavorites()
    {
        Favorites.Clear();
        foreach (var id in _favorites.Favorites(FavoritesManager.MenuBarHost))
            if (_actionsById.TryGetValue(id, out var action))
                Favorites.Add(action);

        OnPropertyChanged(nameof(HasFavorites));
    }

    // File ▸ Open ▸ Asset: pick an asset from the catalog and route it (previewable → Asset Viewer, else
    // the OS default app).
    private async Task OpenAssetAsync()
    {
        // Exclude built-in engine/bridge preview assets — they have no project file to open.
        var entries = _catalog.Entries.Where(entry => !entry.IsBuiltin).ToList();
        if (entries.Count == 0)
        {
            await _popups.InfoAsync("Open Asset", "There are no assets to open (is the engine connected?).")
                .ContinueOnSameContext();
            return;
        }

        var items = entries
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry =>
                new ListPickItem(entry.Id.ToString(CultureInfo.InvariantCulture), entry.Name, entry.Type))
            .ToList();

        var pick = await _popups
            .ShowAsync(_viewModels.Create<ListPickPopupViewModel>("Open Asset", items, "Open"))
            .ContinueOnSameContext();
        if (pick is null)
            return;

        if (_catalog.Find(ulong.Parse(pick.Key, CultureInfo.InvariantCulture)) is { } asset)
            _assetOpener.Open(asset);
    }

    // File ▸ Open ▸ Source: pick a source file (a script or shader) and open it in the Coder editor (which
    // brings a C++ file's header + implementation pair as tabs).
    private async Task OpenSourceAsync()
    {
        var path = await _popups
            .PickFileAsync(
                "Open Source File", "Source files",
                "h", "hpp", "hxx", "hh", "inl", "c", "cc", "cpp", "cxx", "glsl", "frag", "vert", "comp", "geo", "json")
            .ContinueOnSameContext();
        if (path is not null)
            _coder.OpenScript(path);
    }

    // File ▸ Open ▸ Project: pick a project folder and switch to it in-process (gating unsaved changes).
    private async Task OpenProjectAsync()
    {
        if (_projectSwitcher.IsSwitching)
            return;

        var root = await _popups.PickFolderAsync("Open a Toybox project").ContinueOnSameContext();
        if (root is null)
            return;

        if (!ProjectLoader.IsProjectDirectory(root))
        {
            await _popups
                .WarningAsync("Open Project", "That folder isn't a Toybox project (it has no AppSettings.json).")
                .ContinueOnSameContext();
            return;
        }

        if (!_projectSwitcher.IsDifferentProject(root))
            return; // already the open project

        if (!await ConfirmUnsavedAsync().ContinueOnSameContext())
            return;

        await _projectSwitcher.SwitchAsync(root).ContinueOnSameContext();
    }

    // The unsaved-changes gate shared by the project switch: returns true to proceed, false to cancel.
    private async Task<bool> ConfirmUnsavedAsync()
    {
        if (!_projectSwitcher.HasUnsavedChanges && !_workspace.CanSaveFocused)
            return true;

        var choice = await _popups
            .ConfirmAsync<SaveChoice>("Unsaved Changes", "Save changes before switching projects?")
            .ContinueOnSameContext();
        switch (choice)
        {
            case SaveChoice.Save:
                await _workspace.SaveFocusedAsync().ContinueOnSameContext();
                await _projectSwitcher.SaveChangesAsync().ContinueOnSameContext();
                return true;
            case SaveChoice.DontSave:
                return true;
            default:
                return false;
        }
    }

    private async Task AttachAsync()
    {
        _log.Info($"Attaching to a running build on port {Engine.DefaultPort}...");
        await _host.AttachAsync(Engine.DefaultPort).ContinueOnSameContext();
    }

    private async Task BuildEngineAsync()
    {
        var build = await _engineBuilder.BuildAsync(_project, StudioBuild.Mode, CancellationToken.None)
            .ContinueOnSameContext();
        if (!build)
            _log.Error(build.Error!);
    }

    private async Task BuildAppAsync(BuildMode mode)
    {
        var build = await _builder.BuildAsync(_project, mode, CancellationToken.None)
            .ContinueOnSameContext();
        if (!build)
            _log.Error(build.Error!);
    }

    private static KeyChordInputControl Chord(
        InputKey key, bool ctrl = false, bool shift = false, bool alt = false) =>
        new() { Key = key, Ctrl = ctrl, Shift = shift, Alt = alt };
}
