using Avalonia;
using Dock.Avalonia.Controls;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Workspaces;

/// <summary>
/// Binds a <see cref="DockControl"/> to its <see cref="WorkspaceViewModel"/> from XAML —
/// <c>workspaces:WorkspaceBinder.Workspace="{Binding Workspace}"</c> — so the shell needs no
/// code-behind: when the binding delivers the view-model, it takes over the control (factory + saved
/// or default layout).
/// </summary>
public static class WorkspaceBinder
{
    public static readonly AttachedProperty<WorkspaceViewModel?> WorkspaceProperty =
        AvaloniaProperty.RegisterAttached<DockControl, WorkspaceViewModel?>(
            "Workspace", typeof(WorkspaceBinder));

    static WorkspaceBinder()
    {
        WorkspaceProperty.Changed.AddClassHandler<DockControl>(OnWorkspaceChanged);
    }

    public static void SetWorkspace(DockControl control, WorkspaceViewModel? value) =>
        control.SetValue(WorkspaceProperty, value);

    public static WorkspaceViewModel? GetWorkspace(DockControl control) =>
        control.GetValue(WorkspaceProperty);

    private static void OnWorkspaceChanged(DockControl control, AvaloniaPropertyChangedEventArgs args)
    {
        // Fire-and-forget: the bind's only async leg is reading the saved layout off disk; the control
        // shows its inline placeholder layout until the real one swaps in on the UI thread.
        if (args.NewValue is WorkspaceViewModel workspace)
            workspace.BindAsync(control).FireAndForget();
    }
}
