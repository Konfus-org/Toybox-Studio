namespace Toybox.Studio.EngineApi;

/// <summary>
/// The current-project context the <see cref="Session"/> depends on, inverting its dependency on the
/// project manager: it supplies the launch-relevant project info and raises <see cref="Changed"/> when the
/// open project changes (which relaunches the engine in editor mode so its world matches).
/// </summary>
public interface IEngineProject
{
    /// <summary>Raised when the open project changes (a relaunch-worthy change).</summary>
    event Action? Changed;

    /// <summary>The open project's launch info, or null when no project is open.</summary>
    EngineProjectInfo? Current { get; }
}
