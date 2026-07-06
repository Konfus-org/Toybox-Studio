using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Projects;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils;

namespace Toybox.Studio;

/// <summary>
/// Backs the project-picker window: a flat list of the known projects (double-tap a row to open it;
/// its ✕ forgets the row without touching the project on disk) and the Add row beneath, which creates
/// a new project from the bundled template or adds an existing one from disk. The picker doesn't close
/// itself — it completes <see cref="Choice"/> with the chosen project root and the launcher owns
/// showing and closing the window around it. Just closing the window is no choice at all:
/// <see cref="Dismiss"/> records it so the launcher quits instead of opening a studio nobody asked for.
/// </summary>
public sealed partial class ProjectPickerViewModel : ObservableObject
{
    private readonly SettingsManager _settings;
    private readonly ProjectFactory _factory;
    private readonly IStorageProvider _storage;
    private readonly TaskCompletionSource<string?> _choice = new();

    public ProjectPickerViewModel(
        SettingsManager settings, ProjectLoader loader, ProjectFactory factory, IStorageProvider storage)
    {
        _settings = settings;
        _factory = factory;
        _storage = storage;
        // Entries that no longer point at a project on disk are left out of the list (they stay in the
        // settings untouched — a briefly unplugged drive shouldn't unlist its projects for good).
        Projects = new ObservableCollection<ProjectViewModel>(
            settings.Editor.Projects.Recent
                .Where(ProjectLoader.IsProjectDirectory)
                .Select(path => new ProjectViewModel(loader.Load(path))));
    }

    public ObservableCollection<ProjectViewModel> Projects { get; }

    public bool HasProjects => Projects.Count > 0;

    /// <summary>The picked project root — the task the launcher awaits while the window is up.</summary>
    public Task<string?> Choice => _choice.Task;

    /// <summary>True when the picker was closed without any explicit choice (the title-bar X): the user
    /// asked to leave, not to open a studio.</summary>
    public bool WasDismissed { get; private set; }

    /// <summary>Concludes the picker because its window closed; a no-op when a choice was already made.</summary>
    public void Dismiss()
    {
        if (_choice.TrySetResult(null))
            WasDismissed = true;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial ProjectViewModel? SelectedProject { get; set; }

    /// <summary>Shown under the list when adding or creating a project goes wrong.</summary>
    [ObservableProperty]
    public partial string Warning { get; private set; } = string.Empty;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open() => _choice.TrySetResult(SelectedProject!.Path);

    private bool CanOpen() => SelectedProject is not null;

    /// <summary>The explicit way out: concludes with no project at all, same as closing the window.</summary>
    [RelayCommand]
    private void Cancel() => Dismiss();

    /// <summary>Forgets a row: the project drops off the picker (and the persisted list), but nothing
    /// on disk is touched.</summary>
    [RelayCommand]
    private void Remove(ProjectViewModel project)
    {
        Projects.Remove(project);
        OnPropertyChanged(nameof(HasProjects));
        _settings.Editor.Projects.Recent.RemoveAll(p =>
            string.Equals(p, project.Path, StringComparison.OrdinalIgnoreCase));
        _settings.SaveAsync().FireAndForget();
    }

    /// <summary>
    /// Creates a new project from the bundled template: the user picks (or creates, right in the
    /// dialog) an empty folder whose name becomes the project's, and the picker concludes with it.
    /// A bad pick warns inline and leaves the picker open, so the user can just try again.
    /// </summary>
    [RelayCommand]
    private async Task NewAsync()
    {
        var picks = await _storage.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Create the project in an empty folder — its name becomes the project's",
                    AllowMultiple = false,
                })
            .ContinueOnSameContext();
        if (picks.Count == 0 || picks[0].TryGetLocalPath() is not { } root)
            return;

        var created = _factory.Create(root);
        if (created)
            _choice.TrySetResult(created.Value);
        else
            Warning = created.Error!;
    }

    /// <summary>Picks an existing project folder off the disk, starting beside the most recent project.
    /// An invalid pick warns inline and leaves the picker open, so the user can just try again.</summary>
    [RelayCommand]
    private async Task BrowseAsync()
    {
        var startLocation = Projects.Count > 0
            ? await _storage.TryGetFolderFromPathAsync(Projects[0].Path).ContinueOnSameContext()
            : null;
        var picks = await _storage.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Open a Toybox project",
                    AllowMultiple = false,
                    SuggestedStartLocation = startLocation,
                })
            .ContinueOnSameContext();
        if (picks.Count == 0 || picks[0].TryGetLocalPath() is not { } root)
            return;

        if (ProjectLoader.IsProjectDirectory(root))
            _choice.TrySetResult(root);
        else
            Warning = $"'{root}' is not a Toybox project (it has no {ProjectLoader.SettingsFileName}).";
    }
}
