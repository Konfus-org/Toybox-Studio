using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.GameToolbar;

public sealed partial class GameToolbarViewModel : ObservableObject
{
    private readonly EngineSession _session;

    public GameToolbarViewModel(EngineSession session)
    {
        _session = session;
        session.StateChanged += state => Dispatch.To(DispatchContext.UI, () => State = state);
        session.BusyChanged += busy => Dispatch.To(DispatchContext.UI, () => IsBusy = busy);
        session.PausedChanged += paused => Dispatch.To(DispatchContext.UI, () => IsPaused = paused);
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayOrStopCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    [NotifyPropertyChangedFor(nameof(IsPlaying))]
    [NotifyPropertyChangedFor(nameof(PlayOrStopTip))]
    public partial EngineConnectionState State { get; private set; } = EngineConnectionState.Disconnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayOrStopCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool IsPaused { get; private set; }

    public bool IsPlaying => State == EngineConnectionState.Connected;

    public string PlayOrStopTip => State == EngineConnectionState.Connected ? "Stop" : "Play (compiles first)";

    [RelayCommand(CanExecute = nameof(CanPlayOrStop))]
    private Task PlayOrStopAsync()
    {
        return State == EngineConnectionState.Connected
            ? _session.StopAsync()
            : _session.LaunchAsync(CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(CanTogglePause))]
    private Task TogglePauseAsync()
    {
        return _session.SetPausedAsync(!IsPaused);
    }

    private bool CanPlayOrStop()
    {
        return !IsBusy
            && State is EngineConnectionState.Disconnected or EngineConnectionState.Connected;
    }

    private bool CanTogglePause()
    {
        return !IsBusy && State == EngineConnectionState.Connected;
    }
}
