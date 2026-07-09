using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.AppHosting;
using Toybox.Studio.Assets;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Keybindings;
using Toybox.Studio.Logging;
using Toybox.Studio.Projects;
using Toybox.Studio.SettingsEditor;
using Toybox.Studio.Utils;
using Toybox.Studio.Workspaces;
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
    IEventHandler<KeybindingsChanged>
{
    private readonly Project _project;
    private readonly ProjectBuilder _builder;
    private readonly EngineBuilder _engineBuilder;
    private readonly AppHost<Engine> _host;
    private readonly WorkspaceViewModel _workspace;
    private readonly ActionRegistry _actions;
    private readonly EditorKeymap _keymap;
    private readonly Logger _log;
    private readonly Dictionary<string, DockableDescriptor> _windowActions = [];

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
        EventDispatcher events)
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

        RegisterActions();
        WindowItems = BuildWindowItems();
        LayoutItems = BuildLayoutItems();
    }

    /// <summary>The Window menu's rows: every listed dockable.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<WindowMenuItem> WindowItems { get; private set; }

    /// <summary>The Layout menu's rows: save / load / reset the dock arrangement.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<WindowMenuItem> LayoutItems { get; private set; }

    /// <summary>The open project's name, heading its Build submenu. Read once the window binds — the
    /// launch flow loads the project long before the menu exists.</summary>
    public string AppName => _project.Name.Length > 0 ? _project.Name : "App";

    // The fixed items' shortcut hints, re-read whenever the keymap changes.
    public KeyGesture? SettingsGesture => _keymap.GestureFor(ActionIds.OpenSettings);

    public KeyGesture? BuildAppDebugGesture => _keymap.GestureFor(ActionIds.BuildAppDebug);

    public KeyGesture? BuildAppReleaseGesture => _keymap.GestureFor(ActionIds.BuildAppRelease);

    public KeyGesture? BuildEngineGesture => _keymap.GestureFor(ActionIds.BuildEngine);

    public KeyGesture? AttachGesture => _keymap.GestureFor(ActionIds.AttachToRunningBuild);

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

    public void Handle(in KeybindingsChanged evt) => Dispatch.To(DispatchContext.UI, () =>
    {
        WindowItems = BuildWindowItems();
        LayoutItems = BuildLayoutItems();
        OnPropertyChanged(nameof(SettingsGesture));
        OnPropertyChanged(nameof(BuildAppDebugGesture));
        OnPropertyChanged(nameof(BuildAppReleaseGesture));
        OnPropertyChanged(nameof(BuildEngineGesture));
        OnPropertyChanged(nameof(AttachGesture));
    });

    private bool CanBuild => !_isBuilding;

    // The menu items publish their action; execution lives in the Handle above.
    [RelayCommand]
    private void OpenSettings() => _actions.Invoke(ActionIds.OpenSettings);

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
            case ActionIds.OpenSettings:
                _workspace.Open<SettingsViewModel>();
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
            Id = ActionIds.OpenSettings,
            Title = "Open Settings",
            Category = "Edit",
            Icon = Icon.Settings,
            DefaultChords = [Chord(InputKey.Comma, ctrl: true)],
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

    private IReadOnlyList<WindowMenuItem> BuildWindowItems() =>
        [.. _workspace.All
            .Where(descriptor => descriptor.ShowInWindowMenu)
            .Select(descriptor =>
            {
                var id = ActionIds.OpenWindow(descriptor.Key);
                return new WindowMenuItem(
                    descriptor.Title,
                    descriptor.Icon,
                    new RelayCommand(() => _actions.Invoke(id)),
                    _keymap.GestureFor(id));
            })];

    private IReadOnlyList<WindowMenuItem> BuildLayoutItems() =>
    [
        new WindowMenuItem(
            "Save Layout…", Icon.Save,
            new RelayCommand(() => _actions.Invoke(ActionIds.SaveLayout)),
            _keymap.GestureFor(ActionIds.SaveLayout)),
        new WindowMenuItem(
            "Load Layout…", Icon.FolderOpen,
            new RelayCommand(() => _actions.Invoke(ActionIds.LoadLayout)),
            _keymap.GestureFor(ActionIds.LoadLayout)),
        new WindowMenuItem(
            "Reset Layout", Icon.RotateCcw,
            new RelayCommand(() => _actions.Invoke(ActionIds.ResetLayout)),
            _keymap.GestureFor(ActionIds.ResetLayout)),
    ];

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
