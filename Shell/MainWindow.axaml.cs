using Avalonia.Controls;
using Avalonia.Interactivity;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Toybox.Studio.Shell;

public partial class MainWindow : Window
{
    // The floating Settings dock window, if currently open.
    private IDockWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens Settings as a floating, dockable tool (not a modal). If it's already open, just brings it
    /// forward; otherwise a fresh tool is floated into its own window that the user can dock anywhere.
    /// </summary>
    private void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
            return;
        if (DockHost.Layout is not IRootDock root || DockHost.Factory is not { } factory)
            return;

        // Already open: bring the existing window forward instead of opening a second one.
        if (_settingsWindow is not null
            && root.Windows is { } windows
            && windows.Contains(_settingsWindow))
        {
            (_settingsWindow.Host as Window)?.Activate();
            return;
        }

        var tool = new Tool
        {
            Id = "Settings",
            Title = "Settings",
            CanClose = true,
            Content = new SettingsView { DataContext = shell.Settings },
        };

        var window = factory.CreateWindowFrom(tool);
        if (window is null)
            return;

        window.Title = "Settings";
        window.Width = 680;
        window.Height = 620;
        window.X = Position.X + 80;
        window.Y = Position.Y + 80;

        factory.AddWindow(root, window);
        window.Present(isDialog: false);
        _settingsWindow = window;
    }
}
