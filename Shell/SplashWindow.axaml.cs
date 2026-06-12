using Avalonia.Controls;

namespace Toybox.Studio.Shell;

/// <summary>
/// Startup splash that narrates what the app is doing via the shared log console. The main window
/// stays hidden until startup finishes and this window has closed. The <see cref="SplashViewModel"/>
/// is supplied via <see cref="StyledElement.DataContext"/>.
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }
}
