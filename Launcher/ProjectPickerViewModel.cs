using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Projects;
using Toybox.Studio.Utils;

namespace Toybox.Studio;

/// <summary>
/// Backs the project-picker window: the recent projects to reopen (double-tap or the Open button) and
/// the get-started actions beside them (browse the disk for a project, or continue without one). The
/// picker doesn't close itself — it completes <see cref="Choice"/> with the chosen project root (null
/// for none) and the launcher owns showing and closing the window around it.
/// </summary>
public sealed partial class ProjectPickerViewModel : ObservableObject
{
    private readonly IStorageProvider _storage;
    private readonly TaskCompletionSource<string?> _choice = new();

    /// <param name="recentProjects">Recent project roots, newest first; entries that no longer point at
    /// a project on disk are left out of the list (they stay in the settings untouched).</param>
    public ProjectPickerViewModel(
        IEnumerable<string> recentProjects, ProjectLoader loader, IStorageProvider storage)
    {
        _storage = storage;
        Projects = recentProjects
            .Where(ProjectLoader.IsProjectDirectory)
            .Select(p => new ProjectViewModel(loader.Load(p)))
            .ToArray();
    }

    public IReadOnlyList<ProjectViewModel> Projects { get; }

    public bool HasProjects => Projects.Count > 0;

    /// <summary>The picked project root — null when continuing without one (or the window was closed).</summary>
    public Task<string?> Choice => _choice.Task;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    public partial ProjectViewModel? SelectedProject { get; set; }

    /// <summary>Shown under the list when a browsed folder turns out not to be a project.</summary>
    [ObservableProperty]
    public partial string Warning { get; private set; } = string.Empty;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open() => _choice.TrySetResult(SelectedProject!.Path);

    private bool CanOpen() => SelectedProject is not null;

    /// <summary>Picks a project folder off the disk, starting beside the most recent project. An invalid
    /// pick warns inline and leaves the picker open, so the user can just try again.</summary>
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

    [RelayCommand]
    private void Skip() => _choice.TrySetResult(null);
}
