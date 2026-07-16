using Avalonia.Controls;

namespace Toybox.Studio;

/// <summary>
/// Startup splash: a loading bar plus a playful line per startup phase (the real diagnostics go to the
/// unified log). The studio's main window isn't even created until startup finishes and this window has
/// closed. The <see cref="SplashViewModel"/> is supplied via <see cref="StyledElement.DataContext"/>.
/// In a developer (Debug) build the splash also carries the live log console, so it opens taller to fit it;
/// a Release build keeps the compact default height.
/// </summary>
public partial class SplashWindow : Window
{
    // How tall the splash opens in a developer build, where it also shows the log console beneath the
    // loading bars. The Release height is the compact default set in XAML.
    private const int DebugHeight = 540;

    public SplashWindow()
    {
        InitializeComponent();
#if DEBUG
        Height = DebugHeight;
#endif
    }
}
