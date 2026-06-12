using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Widgets.LogConsole;

namespace Toybox.Studio.Shell;

/// <summary>
/// Backs the splash screen: the current step plus the shared log console, so startup lines land in
/// the same console (and TbxStudio.log) the main window shows.
/// </summary>
public sealed partial class SplashViewModel : ObservableObject
{
    public SplashViewModel(LogConsoleViewModel console)
    {
        Console = console;
    }

    /// <summary>
    /// The same console widget shown in the main window; fed by the logging service.
    /// </summary>
    public LogConsoleViewModel Console { get; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Starting…";
}
