using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Project;

/// <summary>
/// Adapts the <see cref="ProjectManager"/> to the engine layer's <see cref="IEngineProject"/>, giving the
/// core engine <see cref="Session"/> the current project's launch info without depending on the project
/// layer. Re-raises the manager's project-changed signal (which relaunches the engine).
/// </summary>
public sealed class EngineProjectAdapter : IEngineProject
{
    private readonly ProjectManager _projects;

    public EngineProjectAdapter(ProjectManager projects)
    {
        _projects = projects;
        _projects.ProjectChanged += _ => Changed?.Invoke();
    }

    public event Action? Changed;

    public EngineProjectInfo? Current =>
        _projects.CurrentProject is { } project
            ? new EngineProjectInfo(
                project.Name,
                project.ModuleName,
                project.AppSettingsPath,
                project.BuildDirectory)
            : null;
}
