using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Widgets.GameToolbar;

public sealed partial class GameToolbarViewModel : ObservableObject
{
    private readonly Session _session;

    /// <summary>
    /// Raised when the user presses Play (the launch branch only). The shell listens so it can re-open the
    /// viewport window if it was closed — the toolbar itself stays free of any docking knowledge.
    /// </summary>
    public event Action? PlayRequested;

    public GameToolbarViewModel(Session session)
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
    public partial ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayOrStopCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool IsPaused { get; private set; }

    public bool IsPlaying => State == ConnectionState.Connected;

    public string PlayOrStopTip => State == ConnectionState.Connected ? "Stop" : "Play (compiles first)";

    [RelayCommand(CanExecute = nameof(CanPlayOrStop))]
    private Task PlayOrStopAsync()
    {
        if (State == ConnectionState.Connected)
            return _session.StopAsync();

        // Fire before the await so the viewport opens immediately, not only once the engine connects.
        PlayRequested?.Invoke();
        return _session.LaunchAsync(CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(CanTogglePause))]
    private Task TogglePauseAsync()
    {
        return _session.SetPausedAsync(!IsPaused);
    }

    private bool CanPlayOrStop()
    {
        return !IsBusy
            && State is ConnectionState.Disconnected or ConnectionState.Connected;
    }

    private bool CanTogglePause()
    {
        return !IsBusy && State == ConnectionState.Connected;
    }
}
