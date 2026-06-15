using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Widgets.Status;
using Toybox.Studio.Widgets.GameToolbar;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Logging;
using Toybox.Studio.Project;
using Toybox.Studio.Workspace;

namespace Toybox.Studio.Shell;

/// <summary>
/// Composes the top-strip widgets into the application shell, hosts the menu actions, and owns the
/// <see cref="Workspace"/> — which manages every dockable panel and the dock layout. Individual panels are
/// no longer referenced here by name; they flow through the workspace's catalog.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Session _session;
    private readonly Logger _log;
    private readonly ProjectManager _projects;
    private readonly FilePicker _filePicker;
    private readonly CommandRunner _commandRunner;

    public ShellViewModel(
        StatusViewModel status,
        GameToolbarViewModel gameToolbar,
        WorkspaceViewModel workspace,
        Session session,
        Logger log,
        ProjectManager projects,
        FilePicker filePicker,
        CommandRunner commandRunner)
    {
        Status = status;
        GameToolbar = gameToolbar;
        Workspace = workspace;

        _session = session;
        _log = log;
        _projects = projects;
        _filePicker = filePicker;
        _commandRunner = commandRunner;

        projects.ProjectChanged += _ => Dispatch.To(DispatchContext.UI, RefreshTitle);
        RefreshTitle();
    }

    public StatusViewModel Status { get; }

    public GameToolbarViewModel GameToolbar { get; }

    /// <summary>The window manager: registered dockables, live dock state, and open/reset/save actions.</summary>
    public WorkspaceViewModel Workspace { get; }

    [ObservableProperty]
    public partial string Title { get; private set; } = "Toybox Studio";

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

    [RelayCommand]
    private Task CompileAsync()
    {
        return _session.CompileProjectAsync(CancellationToken.None);
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

        await _session.ShipAsync(configuration, folder, CancellationToken.None).ContinueOnAnyContext();
    }

    [RelayCommand]
    private async Task DebugEditor()
    {
        var result = _commandRunner.Run("avdt");
        if (!result)
        {
            _log.Error("Failed to launch the editor debug tool. Make sure avdt is on your PATH.");
            Popups.ShowErrorAsync(
                    "Failed To Launch Dev Tools",
                    "Failed to launch Avalonia Developer Tools. Please ensure you have them installed via: dotnet tool install --global AvaloniaUI.DeveloperTools").FireAndForget();
        }
    }

    private void RefreshTitle()
    {
        var project = _projects.CurrentProject;
        Title = project is null ? "Toybox Studio" : $"Toybox Studio — {project.Name}";
    }
}
