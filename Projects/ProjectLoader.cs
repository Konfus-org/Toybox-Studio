using Avalonia.Media.Imaging;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Projects;

/// <summary>
/// Reads a project's info off the disk: point it at a root folder and it fills a <see cref="Project"/>
/// with the name, normalized root path, and icon — either a fresh instance (listing recents in the
/// picker) or the active project the container holds (opening one). Loading never throws: a broken
/// settings or meta file just means no icon; this must never keep the picker from listing a project.
/// </summary>
public sealed class ProjectLoader
{
    /// <summary>The settings file every project carries; its presence (inside <c>.toybox</c>) is what
    /// makes a folder a project.</summary>
    public const string SettingsFileName = "AppSettings.json";

    /// <summary>The project's app-settings file for a given root: it lives in the project's <c>.toybox</c>
    /// folder (the one place a project's editor files and settings live), spelled once here.</summary>
    public static string SettingsPathFor(string root) =>
        Path.Combine(ProjectPaths.BaseDirectoryFor(root), SettingsFileName);

    /// <summary>Whether the folder is a Toybox project (it carries the project settings file in <c>.toybox</c>).</summary>
    public static bool IsProjectDirectory(string path) => File.Exists(SettingsPathFor(path));

    /// <summary>Loads the project at <paramref name="root"/> into a new <see cref="Project"/>.</summary>
    public Project Load(string root)
    {
        var project = new Project();
        Load(root, project);
        return project;
    }

    /// <summary>Loads the project at <paramref name="root"/> into the given instance — how the launch
    /// flow configures the container's active project once the user has picked one.</summary>
    public void Load(string root, Project project)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        project.Path = root;
        project.Name = Path.GetFileName(root);
        project.Module = project.Name;
        project.Icon = LoadIcon(root);
    }

    /// <summary>
    /// The project's README text when it carries one (a root-level README of any extension), trimmed;
    /// null otherwise. The picker previews it in a row's tooltip. Never throws.
    /// </summary>
    public static string? PeekReadme(string root)
    {
        try
        {
            var readme = Directory.EnumerateFiles(root).FirstOrDefault(file =>
                Path.GetFileNameWithoutExtension(file).Equals("README", StringComparison.OrdinalIgnoreCase));
            var text = readme is null ? null : File.ReadAllText(readme).Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The icon the project's settings point at, decoded; null when the project doesn't set
    /// one, the asset can't be found, or it isn't an image Avalonia can decode.</summary>
    private static Bitmap? LoadIcon(string root)
    {
        try
        {
            return FindIconFile(root) is { } file ? new Bitmap(file) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A quick peek into a project's settings for its icon, without the engine: reads the icon asset
    /// handle's id out of the settings file and finds the asset carrying that id in its .meta file
    /// under Assets/.
    /// </summary>
    private static string? FindIconFile(string root)
    {
        var settings = JObject.Parse(File.ReadAllText(SettingsPathFor(root)));
        // A handle serializes as its bare id, but tolerate the expanded { "id": … } object form too.
        var icon = settings["icon"]?["value"];
        var iconId = (icon is JObject expanded ? expanded["id"] : icon)?.Value<ulong?>();
        var assetsDirectory = Path.Combine(root, "Assets");
        if (iconId is null or 0 || !Directory.Exists(assetsDirectory))
            return null;

        foreach (var meta in Directory.EnumerateFiles(assetsDirectory, "*.meta", SearchOption.AllDirectories))
        {
            if (JObject.Parse(File.ReadAllText(meta))["id"]?.Value<ulong?>() != iconId)
                continue;

            var asset = meta[..^".meta".Length];
            return File.Exists(asset) ? asset : null;
        }

        return null;
    }
}
