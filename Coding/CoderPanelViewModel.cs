using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio.Coding;

/// <summary>
/// The dockable host for the code editor: it owns one <see cref="CoderViewModel"/> (<see cref="Editor"/>) that
/// the panel view templates, and does the workspace plumbing the generic editor stays out of — registering the
/// editor's <c>Open</c> as the launcher's current target and claiming the file(s) the open that spawned this
/// panel handed through. Keeping this thin means the same <see cref="CoderView"/> can be embedded elsewhere
/// (an inspector strip) without dragging docking along.
/// </summary>
public sealed class CoderPanelViewModel : ObservableObject, IDisposable
{
    private readonly CoderLauncher _launcher;

    public CoderPanelViewModel(CoderLauncher launcher, ViewModelFactory viewModels)
    {
        _launcher = launcher;
        Editor = viewModels.Create<CoderViewModel>();

        // Route later opens into this panel, and open the file(s) the spawning open handed through.
        launcher.RegisterCurrent(Editor.Open);
        foreach (var path in launcher.TakePending())
            Editor.Open(path);
        // If that open targeted a specific line, scroll the clicked file to it once its tab exists.
        if (launcher.TakePendingReveal() is { } reveal)
            Editor.Open(reveal.File, reveal.Line);
    }

    /// <summary>The generic code editor this panel hosts; the view binds its content to it.</summary>
    public CoderViewModel Editor { get; }

    public void Dispose()
    {
        _launcher.UnregisterCurrent(Editor.Open);
        Editor.Dispose();
    }
}
