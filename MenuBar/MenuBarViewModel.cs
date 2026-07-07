using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.AppHosting;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Projects;
using Toybox.Studio.SettingsEditor;
using Toybox.Studio.Utils;

namespace Toybox.Studio.MenuBar;

/// <summary>
/// The main menu's state and commands. Edit ▸ Settings opens the settings window (one at a time —
/// reopening focuses it); Build ▸ compiles the open project in an explicit mode (its submenu is named
/// after the project, so the menu reads as building THE app) or the engine checkout itself; Debug ▸
/// attaches to an engine already running on the well-known port (e.g. one launched under a native
/// debugger) instead of building and launching our own. Build commands disable while any compile runs,
/// tracked through the dispatched <see cref="BuildStateChanged"/>.
/// </summary>
public sealed partial class MenuBarViewModel :
    ObservableEventSubscriber,
    IEventHandler<BuildStateChanged>
{
    private readonly Project _project;
    private readonly ProjectBuilder _builder;
    private readonly EngineBuilder _engineBuilder;
    private readonly AppHost<Engine> _host;
    private readonly SettingsViewModel _settings;
    private readonly Logger _log;

    private SettingsWindow? _settingsWindow;
    private bool _isBuilding;

    public MenuBarViewModel(
        Project project,
        ProjectBuilder builder,
        EngineBuilder engineBuilder,
        AppHost<Engine> host,
        SettingsViewModel settings,
        Logger log,
        EventDispatcher events)
        : base(events)
    {
        _project = project;
        _builder = builder;
        _engineBuilder = engineBuilder;
        _host = host;
        _settings = settings;
        _log = log;
        // Cancel closes the window this menu opened; the Closed handler then discards the draft.
        settings.CloseRequested += () => _settingsWindow?.Close();
    }

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
    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settings.Open();
        var window = new SettingsWindow { DataContext = _settings };
        window.Closed += (_, _) =>
        {
            _settings.Close();
            _settingsWindow = null;
        };
        _settingsWindow = window;
        window.Show();
    }

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
