using System;
using Avalonia.Controls;
using Toybox.Studio.Widgets.GameToolbar;

namespace Toybox.Studio.Shell;

public partial class MainWindow : Window
{
    private GameToolbarViewModel? _toolbar;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_toolbar is not null)
            _toolbar.PlayRequested -= OnPlayRequested;
        _toolbar = null;

        if (DataContext is not ShellViewModel shell)
            return;

        // Hand the dock host its data-driven factory + layout (saved or default) and let the workspace
        // track live state. From here on, opening/closing/saving panels all go through the workspace.
        shell.Workspace.Bind(DockHost);

        // The transport itself renders inside the Game view now; the shell only listens for Play so it
        // can surface that view.
        _toolbar = shell.GameToolbar;
        _toolbar.PlayRequested += OnPlayRequested;
    }

    // Pressing Play surfaces the Game view (opening or focusing it), so the running game is always visible.
    private void OnPlayRequested()
    {
        if (DataContext is ShellViewModel shell)
            shell.Workspace.EnsureOpen("GameView");
    }
}
