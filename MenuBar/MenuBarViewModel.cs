using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.AppHosting;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Projects;
using Toybox.Studio.SettingsEditor;
using Toybox.Studio.Utils;
using Toybox.Studio.Workspaces;
using Icon = IconPacks.Avalonia.Lucide.PackIconLucideKind;

namespace Toybox.Studio.MenuBar;

/// <summary>
/// The main menu's state and commands. Edit ▸ Settings opens the settings dockable (focusing it when
/// already open); Build ▸ compiles the open project in an explicit mode (its submenu is named after the
/// project, so the menu reads as building THE app) or the engine checkout itself; Debug ▸ attaches to
/// an engine already running on the well-known port (e.g. one launched under a native debugger) instead
/// of building and launching our own; Window ▸ lists every registered dockable (auto-populated from the
/// workspace — Settings opts out) plus the save/load/reset layout actions. Build commands disable while
/// any compile runs, tracked through the dispatched <see cref="BuildStateChanged"/>.
/// </summary>
public sealed partial class MenuBarViewModel :
    ObservableEventSubscriber,
    IEventHandler<BuildStateChanged>
{
    private readonly Project _project;
    private readonly ProjectBuilder _builder;
    private readonly EngineBuilder _engineBuilder;
    private readonly AppHost<Engine> _host;
    private readonly WorkspaceViewModel _workspace;
    private readonly Logger _log;

    private bool _isBuilding;

    public MenuBarViewModel(
        Project project,
        ProjectBuilder builder,
        EngineBuilder engineBuilder,
        AppHost<Engine> host,
        WorkspaceViewModel workspace,
        Logger log,
        EventDispatcher events)
        : base(events)
    {
        _project = project;
        _builder = builder;
        _engineBuilder = engineBuilder;
        _host = host;
        _workspace = workspace;
        _log = log;

        // One entry per registered dockable (Settings opts out via ShowInWindowMenu — it lives under
        // Edit). Built once: the registry is fixed after composition.
        WindowItems = [.. workspace.All
            .Where(descriptor => descriptor.ShowInWindowMenu)
            .Select(descriptor => new WindowMenuItem(
                descriptor.Title, descriptor.Icon, new RelayCommand(() => workspace.Open(descriptor))))];

        LayoutItems =
        [
            new WindowMenuItem("Save Layout…", Icon.Save, workspace.SaveLayoutCommand),
            new WindowMenuItem("Load Layout…", Icon.FolderOpen, workspace.LoadLayoutCommand),
            new WindowMenuItem("Reset Layout", Icon.RotateCcw, workspace.ResetLayoutCommand),
        ];
    }

    /// <summary>The Window menu's rows: every listed dockable.</summary>
    public IReadOnlyList<WindowMenuItem> WindowItems { get; }

    /// <summary>The Layout menu's rows: save / load / reset the dock arrangement.</summary>
    public IReadOnlyList<WindowMenuItem> LayoutItems { get; }

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

    private bool CanBuild => !_isBuilding;

    [RelayCommand]
    private void OpenSettings() => _workspace.Open<SettingsViewModel>();

    [RelayCommand]
    private async Task AttachToRunningBuildAsync()
    {
        _log.Info($"Attaching to a running build on port {Engine.DefaultPort}...");
        await _host.AttachAsync(Engine.DefaultPort).ContinueOnSameContext();
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private Task BuildAppDebugAsync() => BuildAppAsync(BuildMode.Debug);

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private Task BuildAppReleaseAsync() => BuildAppAsync(BuildMode.Release);

    [RelayCommand(CanExecute = nameof(CanBuild))]
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
}
