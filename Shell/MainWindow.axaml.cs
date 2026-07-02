using Avalonia.Controls;

namespace Toybox.Studio.Shell;

/// <summary>
/// The main window: the 3D viewport plus the status bar, bound to a <see cref="ShellViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
