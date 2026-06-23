using Newtonsoft.Json;
using Toybox.Studio.Services.Theming;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.Services.Settings;

/// <summary>
/// Persisted editor-wide configuration, stored as EditorSettings.json in the user's .toybox
/// folder (settings that are not project-specific). Project-specific editor settings live in
/// each project's own EditorSettings.json. Loads and saves itself; missing or invalid settings
/// yield defaults.
/// </summary>
public sealed class EditorSettings
{
    /// <summary>
    /// The root .toybox folder under the user profile where all editor data lives (settings,
    /// themes, logs). Use this instead of hard-coding ".toybox" elsewhere.
    /// </summary>
    public static string BaseDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".toybox");

    private static readonly string FilePath = Path.Combine(BaseDirectory, "EditorSettings.json");

    public EngineEditorSettings Engine { get; set; } = new();

    public BuildEditorSettings Build { get; set; } = new();

    public ProjectEditorSettings Projects { get; set; } = new();

    public ThemeEditorSettings Theme { get; set; } = new();

    public AccessibilityEditorSettings Accessibility { get; set; } = new();

    /// <summary>
    /// Loads the settings from EditorSettings.json, falling back to defaults when the file is missing
    /// or unreadable (the unreadable file is preserved alongside as a *.corrupt breadcrumb).
    /// </summary>
    public static EditorSettings Load()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonConvert.DeserializeObject<EditorSettings>(File.ReadAllText(FilePath)) is { } loaded)
                return loaded;
        }
        catch (Exception)
        {
            // Corrupt settings fall back to defaults. Loaded before the logger exists, so preserve the
            // unreadable file as a visible breadcrumb instead of letting the next Save() silently destroy
            // the user's (possibly recoverable) customizations.
            PreserveCorruptFile();
        }

        return new EditorSettings();
    }

    /// <summary>
    /// Writes the current settings back to EditorSettings.json without blocking the calling (UI) thread:
    /// the JSON is serialized synchronously on the caller (a correct, race-free snapshot) and only the disk
    /// write is awaited. Callers either <c>await</c> this (the Settings panel) or fire-and-forget it (the
    /// event-driven service writes — theme pick, engine path, recent-project list).
    /// </summary>
    public async Task SaveAsync()
    {
        Directory.CreateDirectory(BaseDirectory);
        var json = JsonConvert.SerializeObject(this, Formatting.Indented);
        await File.WriteAllTextAsync(FilePath, json).ConfigureAwait(false);
    }

    private static void PreserveCorruptFile()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Move(FilePath, FilePath + ".corrupt", overwrite: true);
        }
        catch (Exception)
        {
            // Best-effort; if it can't be moved aside, the next Save() overwrites it anyway.
        }
    }
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

/// <summary>
/// Accessibility / comfort preferences. Currently the global animation-intensity dial that scales the
/// editor's micro-animations (button press, toggle tilt, typing wiggle) — see
/// <c>Toybox.Studio.Widgets.Behaviors.Animations.MotionTokens</c>. 0 turns motion off entirely; 1 is the most pronounced.
/// </summary>
public sealed class AccessibilityEditorSettings
{
    /// <summary>
    /// How energetic the editor's micro-animations are, 0 (no motion) to 1 (full). Defaults to a subtle 0.35.
    /// Rendered as a clay slider in Settings via [View("intensitySlider")].
    /// </summary>
    public double AnimationIntensity { get; set; } = 0.35;
}

public sealed class ThemeEditorSettings
{
    /// <summary>
    /// Name of the currently applied theme. There is no light/dark variant — a light/dark pair is just two
    /// themes named by convention, picked from this single list like any other.
    /// </summary>
    [View("ThemePicker")]
    public string Active { get; set; } = Theme.ClayName;
}
