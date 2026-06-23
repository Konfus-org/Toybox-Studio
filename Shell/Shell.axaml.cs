using System;
using Avalonia.Controls;

namespace Toybox.Studio.Shell;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
            return;

        // Hand the dock host its data-driven factory + layout (saved or default) and let the workspace
        // track live state. From here on, opening/closing/saving panels all go through the workspace.
        // The Play/Stop/Pause transport now lives entirely on the Game view's own toolbar.
        shell.Workspace.Bind(DockHost);
    }
}
