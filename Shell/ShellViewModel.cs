using Toybox.Studio.Widgets.EngineConsole;
using Toybox.Studio.Widgets.EngineLauncher;
using Toybox.Studio.Widgets.EngineStatus;
using Toybox.Studio.Widgets.EngineViewport;
using Toybox.Studio.Widgets.EntityInspector;
using Toybox.Studio.Widgets.SceneTree;

namespace Toybox.Studio.Shell;

/// <summary>Composes the independent widgets into the application shell.</summary>
public sealed class ShellViewModel
{
    public ShellViewModel(
        EngineStatusViewModel status,
        EngineConsoleViewModel console,
        EngineLauncherViewModel launcher,
        SceneTreeViewModel sceneTree,
        EntityInspectorViewModel inspector,
        EngineViewportViewModel viewport)
    {
        Status = status;
        Console = console;
        Launcher = launcher;
        SceneTree = sceneTree;
        Inspector = inspector;
        Viewport = viewport;
    }

    public EngineStatusViewModel Status { get; }

    public EngineConsoleViewModel Console { get; }

    public EngineLauncherViewModel Launcher { get; }

    public SceneTreeViewModel SceneTree { get; }

    public EntityInspectorViewModel Inspector { get; }

    public EngineViewportViewModel Viewport { get; }
}
