namespace Toybox.Studio.Projects;

/// <summary>
/// Dispatched after the active project switches in-process (File ▸ Open ▸ Project). The switch drives the
/// engine relaunch imperatively, so this is a UI-refresh signal only: name-bound UI (the Build submenu
/// header, any project-name display) refreshes on it, because the <see cref="Project"/> data type is
/// mutated in place and has no change notification of its own.
/// </summary>
public readonly record struct ProjectChanged(Project Project);
