using Avalonia.Controls;

namespace Toybox.Studio.Shell;

/// <summary>
/// Startup splash: a loading bar plus a playful line per startup phase (the real diagnostics go to the
/// unified log). The studio's main window isn't even created until startup finishes and this window has
/// closed. The <see cref="SplashViewModel"/> is supplied via <see cref="StyledElement.DataContext"/>.
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }
}
