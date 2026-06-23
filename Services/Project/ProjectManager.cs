using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Settings;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.Project;

/// <summary>
/// An opened Toybox project. By convention a project root contains CMakeLists.txt,
/// AppSettings.json, EditorSettings.json, an Assets folder for data, and a Source folder with
/// the app definition at its root plus scripts. The CMake target (and so the app module) is
/// named after the root folder.
/// </summary>
public sealed record ProjectInfo(
    string Name,
    string ModuleName,
    string RootDirectory,
    string AppSettingsPath,
    string EditorSettingsPath,
    string SourceDirectory,
    string AssetsDirectory,
    string BuildDirectory);

/// <summary>
/// Opens projects and tracks the recent list in the editor settings.
/// </summary>
public sealed class ProjectManager
{
    private const int MaxRecentProjects = 10;
    private const string AppSettingsFileName = "AppSettings.json";
    private const string EditorSettingsFileName = "EditorSettings.json";

    private readonly SettingsManager _settings;

    public ProjectManager(SettingsManager settings)
    {
        _settings = settings;

        var lastOpened = settings.Settings.Projects.LastOpened;
        if (!string.IsNullOrEmpty(lastOpened) && (File.Exists(lastOpened) || Directory.Exists(lastOpened)))
            TryOpen(lastOpened, out _);
    }

    /// <summary>Raised when a different project (or world) is opened — listeners may relaunch the engine.</summary>
    public event Action<ProjectInfo?>? ProjectChanged;

    /// <summary>Raised when only the open project's display name changed (e.g. its AppSettings "name" was
    /// edited). Distinct from <see cref="ProjectChanged"/>: this never relaunches the engine.</summary>
    public event Action<ProjectInfo?>? ProjectRenamed;

    public ProjectInfo? CurrentProject { get; private set; }

    /// <summary>
    /// Opens a project from its root folder or its AppSettings.json file.
    /// </summary>
    public bool TryOpen(string path, out string? error)
    {
        error = null;
        var rootDirectory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (rootDirectory is null)
        {
            error = $"'{path}' is not a project folder.";
            return false;
        }

        rootDirectory = Path.GetFullPath(rootDirectory);
        var appSettingsPath = Path.Combine(rootDirectory, AppSettingsFileName);
        if (!File.Exists(appSettingsPath))
        {
            error = $"No {AppSettingsFileName} found in '{rootDirectory}'.";
            return false;
        }

        if (!File.Exists(Path.Combine(rootDirectory, "CMakeLists.txt")))
        {
            error = $"No CMakeLists.txt found in '{rootDirectory}'; projects must be compilable.";
            return false;
        }

        var editorSettingsPath = Path.Combine(rootDirectory, EditorSettingsFileName);
        if (!File.Exists(editorSettingsPath))
            File.WriteAllText(editorSettingsPath, "{\n}\n");

        var moduleName = Path.GetFileName(rootDirectory);
        var name = ReadProjectName(appSettingsPath) ?? moduleName;

        CurrentProject = new ProjectInfo(
            name,
            moduleName,
            rootDirectory,
            appSettingsPath,
            editorSettingsPath,
            Path.Combine(rootDirectory, "Source"),
            Path.Combine(rootDirectory, "Assets"),
            Path.Combine(rootDirectory, "build"));
        RememberProject(rootDirectory);
        ProjectChanged?.Invoke(CurrentProject);
        return true;
    }

    /// <summary>
    /// Stages the bundled default template project into a writable scratch folder and opens it. Used as
    /// the fallback "world to hop into" when no user project is available.
    /// </summary>
    public bool TryOpenDefaultTemplate(out string? error)
    {
        error = null;
        var source = Path.Combine(AppContext.BaseDirectory, "Assets", "Templates", "DefaultProject");
        if (!Directory.Exists(source))
        {
            error = "The bundled default template project could not be found.";
            return false;
        }

        var destination = Path.Combine(EditorSettings.BaseDirectory, "Scratch", "DefaultProject");
        try
        {
            // Copy the template sources over the scratch copy, leaving any existing build/ folder intact.
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }
        catch (Exception exception)
        {
            error = $"Failed to stage the template project: {exception.Message}";
            return false;
        }

        return TryOpen(destination, out error);
    }

    /// <summary>
    /// Re-reads the current project's display name from its AppSettings (it can change via the "name" field)
    /// and updates it in place, raising <see cref="ProjectRenamed"/>. Unlike <see cref="TryOpen"/> / a reopen
    /// this does NOT raise <see cref="ProjectChanged"/>, so it never relaunches the engine — used after the
    /// project's settings are saved (the running engine hot-reloads them itself).
    /// </summary>
    public void RefreshDisplayName()
    {
        if (CurrentProject is not { } project)
            return;

        var name = ReadProjectName(project.AppSettingsPath) ?? project.ModuleName;
        if (string.Equals(name, project.Name, StringComparison.Ordinal))
            return;

        CurrentProject = project with { Name = name };
        ProjectRenamed?.Invoke(CurrentProject);
    }

    private void RememberProject(string rootDirectory)
    {
        var projects = _settings.Settings.Projects;
        projects.LastOpened = rootDirectory;
        projects.Recent.RemoveAll(entry => string.Equals(entry, rootDirectory, StringComparison.OrdinalIgnoreCase));
        projects.Recent.Insert(0, rootDirectory);
        if (projects.Recent.Count > MaxRecentProjects)
            projects.Recent.RemoveRange(MaxRecentProjects, projects.Recent.Count - MaxRecentProjects);

        // Persist off the UI thread: the JSON snapshot is taken synchronously here, only the disk write defers.
        _settings.SaveAsync().FireAndForget();
    }

    private static string? ReadProjectName(string appSettingsPath)
    {
        try
        {
            var json = JObject.Parse(File.ReadAllText(appSettingsPath));
            var name = json.Value<string>("name");
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
