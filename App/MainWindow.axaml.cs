using Avalonia.Controls;

namespace Toybox.Studio;

/// <summary>
/// The main window: the dockable workspace plus the menu and status bars, bound to a
/// <see cref="MainWindowViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
