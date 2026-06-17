using Toybox.Studio.Utils;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Widgets.GameToolbar;

public sealed partial class GameToolbarViewModel : ObservableObject
{
    private readonly Session _session;

    /// <summary>
    /// Raised when the user presses Play (the launch branch only). The shell listens so it can re-open the
    /// viewport window if it was closed — the toolbar itself stays free of any docking knowledge.
    /// </summary>
    public event Action? PlayRequested;

    public GameToolbarViewModel(Session session, EngineWatcher watcher)
    {
        _session = session;
        // The watcher is the single source of "what is the engine doing"; pause is orthogonal and
        // still comes straight off the session.
        watcher.StateChanged += state => Dispatch.To(DispatchContext.UI, () => State = state);
        session.PausedChanged += paused => Dispatch.To(DispatchContext.UI, () => IsPaused = paused);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaying))]
    [NotifyPropertyChangedFor(nameof(PlayOrStopTip))]
    [NotifyCanExecuteChangedFor(nameof(PlayOrStopCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    public partial EngineState State { get; private set; } = EngineState.Off;

    [ObservableProperty]
    public partial bool IsPaused { get; private set; }

    // Play mode (running the game loop), distinct from the engine connection. The engine runs in
    // editor mode (Ready) while not playing.
    public bool IsPlaying => State == EngineState.Playing;

    public string PlayOrStopTip => IsPlaying ? "Stop" : "Play";

    [RelayCommand(CanExecute = nameof(CanPlayOrStop))]
    private Task PlayOrStopAsync()
    {
        if (IsPlaying)
            return _session.StopPlayAsync();

        // Fire before the await so the game view surfaces immediately as play begins.
        PlayRequested?.Invoke();
        return _session.StartPlayAsync();
    }

    [RelayCommand(CanExecute = nameof(CanTogglePause))]
    private Task TogglePauseAsync()
    {
        return _session.SetPausedAsync(!IsPaused);
    }

    // Play/Stop is available whenever the engine is running (editor mode or already playing); it
    // toggles play, it no longer launches or kills the engine.
    private bool CanPlayOrStop()
    {
        return State is EngineState.Ready or EngineState.Playing;
    }

    private bool CanTogglePause()
    {
        return IsPlaying;
    }
}
