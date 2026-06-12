using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services;

/// <summary>
/// Editable subset of a project's AppSettings.json.
/// </summary>
public sealed record AppSettingsInfo(string Name, List<string> Plugins);

/// <summary>
/// Persisted editor-wide configuration, stored as EditorSettings.json in the user's .toybox
/// folder (settings that are not project-specific). Project-specific editor settings live in
/// each project's own EditorSettings.json.
/// </summary>
public sealed class EditorSettings
{
    public EngineEditorSettings Engine { get; set; } = new();

    public BuildEditorSettings Build { get; set; } = new();

    public ProjectEditorSettings Projects { get; set; } = new();

    public ThemeEditorSettings Theme { get; set; } = new();
}

/// <summary>
/// How the editor compiles a project's native code (engine + project, built in-tree via CMake).
/// </summary>
public sealed class BuildEditorSettings
{
    /// <summary>
    /// Which C++ toolchain to build with: "Auto" (MSVC on Windows, Clang elsewhere), "MSVC", or
    /// "Clang". Changing this reconfigures the build tree from clean on the next compile.
    /// </summary>
    public string Compiler { get; set; } = "Auto";

    /// <summary>
    /// Build targets in parallel (faster); turn off for serial, easier-to-follow build output.
    /// </summary>
    public bool Parallel { get; set; } = true;

    /// <summary>
    /// Echo the compiler/linker command lines into the build log (useful when diagnosing builds).
    /// </summary>
    public bool Verbose { get; set; }
}

public sealed class EngineEditorSettings
{
    public string SourcePath { get; set; } = "";

    public int ConnectTimeoutSeconds { get; set; } = 30;

    public bool HideEngineWindow { get; set; } = true;

    public bool RestartOnCrash { get; set; } = true;

    /// <summary>
    /// When true, the editor launches the engine automatically at startup (into the active world, or the
    /// bundled template world if no project is open); when false the engine is launched on demand.
    /// </summary>
    public bool AutoLaunchEngine { get; set; } = true;
}

public sealed class ProjectEditorSettings
{
    public string LastOpened { get; set; } = "";

    public List<string> Recent { get; set; } = [];
}

public sealed class ThemeEditorSettings
{
    /// <summary>
    /// Dark or Light — which of the two chosen themes is currently applied. Serialized as its readable
    /// name; legacy "Dark"/"Light" string files parse through the same converter.
    /// </summary>
    [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
    public ThemeMode Variant { get; set; } = ThemeMode.Dark;

    [View("themePicker")]
    public string DarkTheme { get; set; } = Theme.DarkName;

    [View("themePicker")]
    public string LightTheme { get; set; } = Theme.LightName;

    [JsonIgnore]
    public bool IsLight => Variant == ThemeMode.Light;
}

/// <summary>
/// The editor's settings hub: loads and saves the global editor settings (EditorSettings.json),
/// and reads/writes a project's app settings (AppSettings.json). Missing or invalid editor settings
/// yield defaults.
/// </summary>
public sealed class Settings
{
    /// <summary>
    /// The root .toybox folder under the user profile where all editor data lives (settings,
    /// themes, logs). Use this instead of hard-coding ".toybox" elsewhere.
    /// </summary>
    public static string BaseDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".toybox");

    private static readonly string SettingsPath =
        Path.Combine(BaseDirectory, "EditorSettings.json");

    public Settings()
    {
        Editor = Load();
    }

    /// <summary>
    /// The global editor settings (engine, projects, theme), loaded from EditorSettings.json.
    /// </summary>
    public EditorSettings Editor { get; }

    public void Save()
    {
        Directory.CreateDirectory(BaseDirectory);
        File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(Editor, Formatting.Indented));
    }

    /// <summary>
    /// The given project's app settings (name + plugin list), or null when unavailable.
    /// </summary>
    public AppSettingsInfo? ReadAppSettings(ProjectInfo? project)
    {
        if (project is null)
            return null;

        try
        {
            var json = JObject.Parse(File.ReadAllText(project.AppSettingsPath));
            var plugins = json["plugins"]?.Values<string>().OfType<string>().ToList() ?? [];
            return new AppSettingsInfo(json.Value<string>("name") ?? project.Name, plugins);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes name + plugins back into the project's AppSettings.json.
    /// </summary>
    public bool TrySaveAppSettings(
        ProjectInfo? project,
        string name,
        IReadOnlyList<string> plugins,
        out string? error)
    {
        error = null;
        if (project is null)
        {
            error = "No project is open.";
            return false;
        }

        try
        {
            var json = JObject.Parse(File.ReadAllText(project.AppSettingsPath));
            json["name"] = name;
            json["plugins"] = new JArray(plugins);
            File.WriteAllText(project.AppSettingsPath, json.ToString(Formatting.Indented));
            return true;
        }
        catch (Exception exception)
        {
            error = $"Failed to save app settings: {exception.Message}";
            return false;
        }
    }

    /// <summary>
    /// Reads the project's full AppSettings.json as a JObject, or null.
    /// </summary>
    public JObject? ReadAppSettingsJson(ProjectInfo? project)
    {
        if (project is null)
            return null;

        try
        {
            return JObject.Parse(File.ReadAllText(project.AppSettingsPath));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes a full AppSettings.json document back to the project.
    /// </summary>
    public bool TrySaveAppSettingsJson(ProjectInfo? project, JObject json, out string? error)
    {
        error = null;
        if (project is null)
        {
            error = "No project is open.";
            return false;
        }

        try
        {
            File.WriteAllText(project.AppSettingsPath, json.ToString(Formatting.Indented));
            return true;
        }
        catch (Exception exception)
        {
            error = $"Failed to save app settings: {exception.Message}";
            return false;
        }
    }

    private static EditorSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonConvert.DeserializeObject<EditorSettings>(File.ReadAllText(SettingsPath));
                if (loaded is not null)
                    return loaded;
            }
        }
        catch (Exception)
        {
            // Corrupt settings fall back to defaults; the next Save() rewrites the file.
        }

        return new EditorSettings();
    }
}
