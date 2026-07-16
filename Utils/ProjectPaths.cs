namespace Toybox.Studio.Utils;

/// <summary>
/// The single registry of every well-known location inside the open project's <c>.toybox</c> folder — the
/// project-local counterpart to <see cref="PathsCatalog"/> (which owns the user-global <c>~/.toybox</c>
/// data). Both live in the lowest layer so any feature can resolve a path without depending on the
/// higher project layer; this one carries the open project's root as a plain string that the project
/// loader pushes in on open (and on an in-process switch), rather than depending on the <c>Project</c>
/// type itself. Every path is computed on demand off <see cref="Root"/>, so it is safe to construct and
/// inject before a project is picked — consumers (dock layouts, node layouts, project settings) read a
/// path only at I/O time, which is always after the loader has set the root. <see cref="Root"/> is empty
/// until then; guard on it before writing so a stray write can't land in a relative <c>.toybox</c>.
/// </summary>
public sealed class ProjectPaths
{
    /// <summary>The name of the per-project folder, spelled once here.</summary>
    public const string FolderName = ".toybox";

    /// <summary>The open project's root folder, set by the project loader on open/switch; empty before
    /// any project is loaded.</summary>
    public string Root { get; set; } = string.Empty;

    /// <summary>The <c>.toybox</c> folder for an arbitrary project root — for code that works off a
    /// candidate path rather than the open project (project detection, the picker, the icon peek).</summary>
    public static string BaseDirectoryFor(string root) => Path.Combine(root, FolderName);

    /// <summary>The open project's <c>.toybox</c> folder, where its editor files and settings live.</summary>
    public string BaseDirectory => BaseDirectoryFor(Root);

    /// <summary>The project-scoped editor settings file (build, gizmos, asset browser), split off from the
    /// user-global EditorSettings.json and owned by the settings manager.</summary>
    public string ProjectSettingsFile => Path.Combine(BaseDirectory, "ProjectSettings.json");

    /// <summary>The folder holding this project's dock layouts (the auto-saved working layout and any
    /// user-named ones).</summary>
    public string LayoutsDirectory => Path.Combine(BaseDirectory, "Layouts");

    /// <summary>The folder holding the viewport node-overlay layouts, one <c>&lt;worldId&gt;.json</c> per
    /// world.</summary>
    public string NodesDirectory => Path.Combine(BaseDirectory, "nodes");
}
