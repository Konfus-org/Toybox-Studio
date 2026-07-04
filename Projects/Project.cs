using Avalonia.Media.Imaging;

namespace Toybox.Studio.Projects;

/// <summary>
/// A Toybox project as pure data: what it is called, where it lives, and how it presents itself.
/// The container holds one instance as the active project — the one the studio has open — which the
/// launch flow populates through the <see cref="ProjectLoader"/>; services that need the open project
/// take it in their constructors. The project itself has no behavior: building and shipping are the
/// callers' business, composed from <see cref="ProjectBuilder"/> and <see cref="ProjectShipper"/>.
/// </summary>
public sealed class Project
{
    /// <summary>The project's display name; its root folder's name by convention.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The app module the engine hosts — the CMake target the project's build produces. By
    /// convention it is named after the project's root folder, so it loads as the name.</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>The project's root folder; empty while no project has been loaded.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The project's icon, peeked out of its settings; null when it doesn't set one (or no
    /// project is loaded) — display falls back to a default.</summary>
    public Bitmap? Icon { get; set; }

    // TODO: Settings — needs the engine asset API, so the project-settings asset can live here too.
}
