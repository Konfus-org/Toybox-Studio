namespace Toybox.Studio.Utils;

/// <summary>
/// The single registry of every well-known user-global filesystem location Toybox uses — the one place a
/// path is spelled, so nothing hard-codes ".toybox", a folder name, or a file name. Registered as a
/// singleton service and injected wherever a global path is needed. It depends on nothing and is cheap to
/// construct, so the crash handler — which runs before the container exists — can also <c>new</c> one
/// directly. Two roots: the user's ~/.toybox data folder (settings, keybindings, themes, favorites, logs,
/// crash reports) and the app's install directory (the bundled project template). Project-local paths
/// (dock layouts, node layouts, per-project settings) live under the open project's own <c>.toybox</c>
/// folder and are spelled by <see cref="Toybox.Studio.Projects.ProjectPaths"/> instead.
/// </summary>
public sealed class PathsCatalog
{
    /// <summary>The root .toybox folder under the user profile where all editor data lives.</summary>
    public string BaseDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".toybox");

    /// <summary>The editor-wide settings file (plain JSON), loaded and saved by the settings manager.</summary>
    public string EditorSettingsFile => Path.Combine(BaseDirectory, "EditorSettings.json");

    /// <summary>The editor's keymap — a real <c>.inputmap</c> the engine could load verbatim.</summary>
    public string EditorKeymapFile => Path.Combine(BaseDirectory, "EditorKeybindings.inputmap");

    /// <summary>The folder holding the user's <c>Theme.json</c> files.</summary>
    public string ThemesDirectory => Path.Combine(BaseDirectory, "Themes");

    /// <summary>The folder holding the per-surface starred-item files (one <c>&lt;host&gt;.json</c> per
    /// menu bar / context menu), owned by the favorites store.</summary>
    public string FavoritesDirectory => Path.Combine(BaseDirectory, "Favorites");

    /// <summary>The folder holding the rotated <c>TbxStudio.log</c> files.</summary>
    public string LogsDirectory => Path.Combine(BaseDirectory, "Logs");

    /// <summary>The append-only crash report, written synchronously while the process is going down.</summary>
    public string CrashLogFile => Path.Combine(LogsDirectory, "TbxStudio.crash.log");

    /// <summary>The bundled default-project template copied out to create a new project. Rooted in the
    /// app's install directory, not the user profile.</summary>
    public string DefaultProjectTemplate =>
        Path.Combine(AppContext.BaseDirectory, "Templates", "Projects", "Default");
}
