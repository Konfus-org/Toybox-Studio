using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.EngineLauncher;

public sealed partial class EngineLauncherViewModel : ObservableObject
{
    private readonly EngineSessionService _session;

    public EngineLauncherViewModel(EngineSessionService session)
    {
        _session = session;
        session.StateChanged += state => Dispatcher.UIThread.Post(() => State = state);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LaunchCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial EngineConnectionState State { get; private set; } = EngineConnectionState.Disconnected;

    [RelayCommand(CanExecute = nameof(CanLaunch))]
    private Task LaunchAsync()
    {
        return _session.LaunchAsync(CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync()
    {
        return _session.StopAsync();
    }

    private bool CanLaunch()
    {
        return State == EngineConnectionState.Disconnected;
    }

    private bool CanStop()
    {
        return State != EngineConnectionState.Disconnected;
    }
}
