using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using Toybox.Studio.Keybindings;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.Settings;

/// <summary>
/// Persisted editor-wide configuration (settings that are not project-specific) — plain data only.
/// <see cref="SettingsManager"/> owns loading and saving it (EditorSettings.json in the user's
/// .toybox folder); missing or invalid settings yield these defaults.
/// </summary>
public sealed class EditorSettings
{
    public EngineEditorSettings Engine { get; set; } = new();

    /// <summary>
    /// How the editor builds this project's native code. NOT part of this (user-global) file's persistence —
    /// it is project-scoped and lives in the project's <c>.toybox/ProjectSettings.json</c> (owned by
    /// <see cref="SettingsManager"/>, the same way <see cref="Keybindings"/> lives in its own file). It stays
    /// a property here so the Settings grid and every consumer still reach it through the live settings.
    /// </summary>
    [JsonIgnore]
    public BuildEditorSettings Build { get; set; } = new();

    public ProjectEditorSettings Projects { get; set; } = new();

    public ScriptingEditorSettings Scripting { get; set; } = new();

    /// <summary>The viewport gizmo's snapping. Project-scoped like <see cref="Build"/> — persisted in the
    /// project's <c>.toybox/ProjectSettings.json</c>, not this global file.</summary>
    [JsonIgnore]
    public GizmoEditorSettings Gizmos { get; set; } = new();

    /// <summary>The Asset Browser's categories and open-assets mode. Project-scoped like <see cref="Build"/> —
    /// persisted in the project's <c>.toybox/ProjectSettings.json</c>, not this global file.</summary>
    [JsonIgnore]
    public EditorAssetSettings EditorAssetSettings { get; set; } = new();

    public ThemeEditorSettings Theme { get; set; } = new();

    public AccessibilityEditorSettings Accessibility { get; set; } = new();

    /// <summary>
    /// The editor's keybindings, surfaced as a settings section so the grid reflects them like any
    /// other data. NOT part of this file's persistence — the keymap lives in its own
    /// <c>.inputmap</c> asset (the Keybindings layer's <c>EditorKeymap</c> owns it), and the
    /// settings window fills this list for the grid session and commits it back on save. The
    /// registry defines the editor's actions, so the list arrives read-only (no adding or removing
    /// rows) and only each entry's chord is editable; project input map assets are the fully
    /// editable surface.
    /// </summary>
    [JsonIgnore]
    [Icon("Keyboard")]
    public IReadOnlyList<Keybinding> Keybindings { get; set; } = [];
}

/// <summary>
/// Which C++ toolchain the editor builds native code with. Mirrors the CMake layer's
/// CompilerPreference member-for-member — kept as its own enum here (rather than referencing the CMake
/// project) so the settings layer stays independent; the build services map it.
/// </summary>
public enum CompilerChoice
{
    /// <summary>MSVC on Windows, Clang elsewhere.</summary>
    Auto,

    /// <summary>The Microsoft Visual C++ toolchain (Windows only).</summary>
    Msvc,

    /// <summary>The LLVM Clang toolchain.</summary>
    Clang,
}

/// <summary>
/// How the editor compiles a project's native code (engine + project, built in-tree via CMake).
/// </summary>
[Icon("Hammer")]
public sealed class BuildEditorSettings
{
    /// <summary>
    /// Which C++ toolchain to build with. Changing this reconfigures the build tree from clean on the
    /// next compile. Persisted by name so the settings file stays hand-readable.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public CompilerChoice Compiler { get; set; } = CompilerChoice.Auto;

    /// <summary>
    /// Build targets in parallel (faster); turn off for serial, easier-to-follow build output.
    /// </summary>
    public bool Parallel { get; set; } = true;

    /// <summary>
    /// Echo the compiler/linker command lines into the build log (useful when diagnosing builds).
    /// </summary>
    public bool Verbose { get; set; }
}

[Icon("Cog")]
public sealed class EngineEditorSettings
{
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// The git URL the engine checkout is cloned from when none is found on startup. Empty by default —
    /// the launcher asks for it the first time it needs to download the engine, then remembers it here.
    /// </summary>
    public string RepoUrl { get; set; } = string.Empty;

    public int ConnectTimeoutSeconds { get; set; } = 30;

    public bool HideEngineWindow { get; set; } = true;

    public bool RestartOnCrash { get; set; } = true;

    /// <summary>
    /// When true, the editor launches the engine automatically at startup (into the active world, or the
    /// bundled template world if no project is open); when false the engine is launched on demand.
    /// </summary>
    public bool AutoLaunchEngine { get; set; } = true;
}

[Icon("FolderOpen")]
public sealed class ProjectEditorSettings
{
    // Bookkeeping the launch flow maintains, not preferences — hidden from the Settings grid.
    [Hidden]
    public string LastOpened { get; set; } = string.Empty;

    [Hidden]
    public List<string> Recent { get; set; } = [];
}

/// <summary>
/// The in-Studio C++ script editor (the inline Script-tab strip and the popped-out window). These apply to
/// both surfaces so the two stay consistent.
/// </summary>
[Icon("Code")]
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
/// The viewport transform gizmo's snapping: whether drags snap by default (the viewport toolbar's
/// magnet toggle — the snap-hold key, Ctrl out of the box, momentarily inverts it) and the per-kind
/// steps a snapped drag quantizes to.
/// </summary>
[Icon("Magnet")]
public sealed class GizmoEditorSettings
{
    /// <summary>Snap gizmo drags to the steps below without holding the snap key (holding it then
    /// gives a free drag).</summary>
    public bool SnapEnabled { get; set; }

    /// <summary>The translate grid step, in world units.</summary>
    public double TranslateStep { get; set; } = 0.5;

    /// <summary>The rotate step, in degrees.</summary>
    public double RotateStepDegrees { get; set; } = 15;

    /// <summary>The scale factor step.</summary>
    public double ScaleStep { get; set; } = 0.1;
}

/// <summary>
/// Accessibility / comfort preferences. Currently the global animation-intensity dial that scales the
/// editor's micro-animations (button press, toggle tilt, typing wiggle) — see
/// <c>Toybox.Studio.Behaviors.Animations.MotionTokens</c>. 0 turns motion off entirely; 1 is the most pronounced.
/// </summary>
[Icon("PersonStanding")]
public sealed class AccessibilityEditorSettings
{
    /// <summary>
    /// How energetic the editor's micro-animations are, 0 (no motion) to 1 (full). Defaults to a subtle 0.35.
    /// The Settings window live-previews edits to it, so the motion is felt as the value moves.
    /// </summary>
    [Slider(0, 1)]
    public double AnimationIntensity { get; set; } = 0.35;

    /// <summary>
    /// How thick the viewport transform-gizmo handles draw, as a multiplier (1 = the default slender
    /// look; raise it for more visible handles). Purely visual — the pointer's grab distances are
    /// unchanged.
    /// </summary>
    public double GizmoThickness { get; set; } = 1.0;
}

[Icon("Palette")]
public sealed class ThemeEditorSettings
{
    // The default theme name. Kept as a literal here (rather than referencing the theming layer's
    // Theme.ClayName) so the settings layer stays independent of Theming; the theme repository resolves it.
    private const string DefaultTheme = "Claymorphism";

    /// <summary>
    /// Name of the currently applied theme. There is no light/dark variant — a light/dark pair is just two
    /// themes named by convention, picked from this single list like any other.
    /// </summary>
    public string Active { get; set; } = DefaultTheme;
}

