using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services;
using Toybox.Studio.Widgets.LogConsole;
using Toybox.Studio.Widgets.Status;
using Toybox.Studio.Widgets.Viewport;
using Toybox.Studio.Widgets.EntityInspector;
using Toybox.Studio.Widgets.GameToolbar;
using Toybox.Studio.Widgets.WorldTree;

namespace Toybox.Studio.Shell;

/// <summary>
/// Composes the independent widgets into the application shell and hosts menu actions.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly EngineSession _session;
    private readonly Logger _log;
    private readonly ProjectManager _projects;
    private readonly FilePicker _filePicker;

    public ShellViewModel(
        StatusViewModel status,
        LogConsoleViewModel console,
        WorldTreeViewModel worldTree,
        EntityInspectorViewModel inspector,
        ViewportViewModel viewport,
        GameToolbarViewModel gameToolbar,
        EngineSession session,
        Logger log,
        ProjectManager projects,
        FilePicker filePicker,
        SettingsViewModel settings)
    {
        Status = status;
        Console = console;
        WorldTree = worldTree;
        Inspector = inspector;
        Viewport = viewport;
        GameToolbar = gameToolbar;
        _session = session;
        _log = log;
        _projects = projects;
        _filePicker = filePicker;
        Settings = settings;

        projects.ProjectChanged += _ => Dispatch.To(DispatchContext.UI, RefreshTitle);
        RefreshTitle();
    }

    public StatusViewModel Status { get; }

    public LogConsoleViewModel Console { get; }

    public WorldTreeViewModel WorldTree { get; }

    public EntityInspectorViewModel Inspector { get; }

    public ViewportViewModel Viewport { get; }

    public GameToolbarViewModel GameToolbar { get; }

    /// <summary>
    /// Settings VM, shown in a floating dockable tool opened from the toolbar.
    /// </summary>
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    public partial string Title { get; private set; } = "Toybox Studio";

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var path = await _filePicker.PickFolderAsync("Open Toybox Project").ContinueOnAnyContext();
        if (path is null)
            return;

        if (!_projects.TryOpen(path, out var error))
            _log.Error(error ?? "Failed to open the project.");
    }

    [RelayCommand]
    private Task CompileAsync()
    {
        return _session.CompileProjectAsync(CancellationToken.None);
    }

    [RelayCommand]
    private Task AttachAsync()
    {
        return _session.AttachAsync(EngineInstanceDetector.DefaultEnginePort);
    }

    [RelayCommand]
    private async Task ShipAsync(string configuration)
    {
        var folder = await _filePicker.PickFolderAsync($"Choose {configuration} Ship Output Folder")
            .ContinueOnAnyContext();
        if (folder is null)
            return;

        await _session.ShipAsync(configuration, folder, CancellationToken.None).ContinueOnAnyContext();
    }

    private void RefreshTitle()
    {
        var project = _projects.CurrentProject;
        Title = project is null ? "Toybox Studio" : $"Toybox Studio — {project.Name}";
    }
}
