using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Console;

namespace Toybox.Studio.Shell;

/// <summary>
/// Backs the splash screen: the current startup step plus the shared log console, so startup lines
/// land in the same console (and TbxStudio.log) the rest of the app uses.
/// </summary>
public sealed partial class SplashViewModel : ObservableObject
{
    public SplashViewModel(ConsoleViewModel console) => Console = console;

    /// <summary>The shared console fed by the logging service.</summary>
    public ConsoleViewModel Console { get; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Starting…";
}
