namespace Toybox.Studio.Projects;

/// <summary>
/// The configuration a project is built in. The engine is built in-tree with the project, so the mode
/// also selects the engine binary the build produces. Member names match the CMake configuration names
/// exactly — <see cref="ProjectBuilder"/> puts them on the wire as-is.
/// </summary>
public enum BuildMode
{
    Debug,
    Release,
}
