namespace Toybox.Studio.Settings;

/// <summary>
/// Persisted editor-wide configuration (settings that are not project-specific) — plain data only.
/// <see cref="SettingsManager"/> owns loading and saving it (EditorSettings.json in the user's
/// .toybox folder); missing or invalid settings yield these defaults.
/// </summary>
public sealed class EditorSettings
{
    public EngineEditorSettings Engine { get; set; } = new();

    public BuildEditorSettings Build { get; set; } = new();

    public ProjectEditorSettings Projects { get; set; } = new();

    public ScriptingEditorSettings Scripting { get; set; } = new();

    public ThemeEditorSettings Theme { get; set; } = new();

    public AccessibilityEditorSettings Accessibility { get; set; } = new();
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
    public string SourcePath { get; set; } = string.Empty;

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
    public string LastOpened { get; set; } = string.Empty;

    public List<string> Recent { get; set; } = [];
}

/// <summary>
/// The in-Studio C++ script editor (the inline Script-tab strip and the popped-out window). These apply to
/// both surfaces so the two stay consistent.
/// </summary>
public sealed class ScriptingEditorSettings
{
    /// <summary>
    /// Recompile the scripts on save so the running engine hot-reloads them (the editor's lightning-bolt
    /// toggle). Off by default — a save then triggers an incremental project build.
    /// </summary>
    public bool HotReloadOnSave { get; set; }

    /// <summary>Editor font size, in points.</summary>
    public int FontSize { get; set; } = 13;

    /// <summary>Soft-wrap long lines instead of scrolling horizontally.</summary>
    public bool WordWrap { get; set; }

    /// <summary>Show the minimap (the code overview strip) in the popped-out editor.</summary>
    public bool ShowMinimap { get; set; } = true;
}

/// <summary>
/// Accessibility / comfort preferences. Currently the global animation-intensity dial that scales the
/// editor's micro-animations (button press, toggle tilt, typing wiggle) — see
/// <c>Toybox.Studio.Behaviors.Animations.MotionTokens</c>. 0 turns motion off entirely; 1 is the most pronounced.
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
    // The default theme name. Kept as a literal here (rather than referencing the theming layer's
    // Theme.ClayName) so the settings layer stays independent of Theming; the theme repository resolves it.
    private const string DefaultTheme = "Claymorphism";

    /// <summary>
    /// Name of the currently applied theme. There is no light/dark variant — a light/dark pair is just two
    /// themes named by convention, picked from this single list like any other. (The Settings panel renders
    /// the theme selection as its own section, so no [View] tag is needed on the data here.)
    /// </summary>
    public string Active { get; set; } = DefaultTheme;
}

