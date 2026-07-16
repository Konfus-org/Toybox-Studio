using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Utils.Composition;
using Toybox.Studio.Viewport;

namespace Toybox.Studio.GameViewer;

/// <summary>
/// The Game Viewer dockable's view-model: a single <see cref="ViewKind.Game"/> viewport (which mirrors
/// the running game's camera into its own GPU-shared target) plus the play / pause / stop / next-frame
/// transport that drives the engine's run state through <see cref="Engine.SetStateAsync"/> and
/// <see cref="Engine.StepAsync"/>. It owns no world state: entering play snapshots the world engine-side
/// and stopping restores it, so gameplay edits never persist and the simulation (which streams back over
/// the no-dirty <c>Hydrate</c> path) never marks the editor dirty. The transport's enabled state is
/// re-derived from the engine's <see cref="EngineState"/> on every <see cref="EngineStateChanged"/>.
/// </summary>
public sealed partial class GameViewerViewModel : ObservableEventSubscriber,
    IEventHandler<EngineStateChanged>,
    IDisposable
{
    private readonly Engine _engine;

    // The engine's derived run state, copied here on the (UI-thread) EngineStateChanged events so the
    // transport's CanExecute predicates re-evaluate without holding the engine's private RunState.
    private EngineState _state;

    public GameViewerViewModel(Engine engine, EventDispatcher events, ViewModelFactory viewModels) : base(events)
    {
        _engine = engine;
        _state = engine.State;

        // A plain game viewport: no pick handler (nothing to click-select in play mode) and no overlay
        // toolbars (the transport lives in the panel chrome, not floating over the image). Its stream
        // starts a dedicated engine view on the game camera. The empty-state message is the one runtime
        // argument; the factory injects the viewport's EventDispatcher/Logger.
        Viewport = viewModels.Create<ViewportViewModel>(ViewKind.Game, "Press Play to start.");
        Viewport.Prepare(new ViewportStream(engine, events, ViewKind.Game));
    }

    /// <summary>The embedded game surface — its own engine view + shared GPU texture.</summary>
    public ViewportViewModel Viewport { get; }

    // The engine's run state, mirrored from EngineState (the derivation Engine.RunState makes internally):
    // paused wins over playing, and editing means connected-and-not-running.
    private bool IsPlaying => _state.Phase == EnginePhase.Playing && !_state.IsPaused;
    private bool IsPaused => _state.IsPaused;
    private bool IsEditing => _state.IsConnected && !IsPlaying && !IsPaused;

    // Play enters from editing or resumes from paused; pause only while running; stop from either play
    // state; step only while paused (there is nothing to single-step otherwise).
    private bool CanPlay => IsEditing || IsPaused;
    private bool CanPause => IsPlaying;
    private bool CanStop => IsPlaying || IsPaused;
    private bool CanStep => IsPaused;

    /// <summary>Enter play (from editing) or resume (from paused). The engine snapshots the world on the
    /// editing→playing edge, so nothing the session does survives a later Stop.</summary>
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private Task PlayAsync() => _engine.SetStateAsync(EngineRunState.Playing);

    /// <summary>Freeze the simulation while the view keeps rendering.</summary>
    [RelayCommand(CanExecute = nameof(CanPause))]
    private Task PauseAsync() => _engine.SetStateAsync(EngineRunState.Paused);

    /// <summary>Leave play: the engine restores the pre-play world and resets physics/scripts, discarding
    /// every gameplay edit.</summary>
    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync() => _engine.SetStateAsync(EngineRunState.Editing);

    /// <summary>Advance the paused simulation by exactly one fixed tick.</summary>
    [RelayCommand(CanExecute = nameof(CanStep))]
    private Task StepFrameAsync() => _engine.StepAsync();

    // Dispatched on the UI thread by the engine: refresh which transport buttons are enabled.
    public void Handle(in EngineStateChanged evt)
    {
        _state = evt.State;
        PlayCommand.NotifyCanExecuteChanged();
        PauseCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        StepFrameCommand.NotifyCanExecuteChanged();
    }

    public override void Dispose()
    {
        base.Dispose();      // unregisters the event handlers
        Viewport.Dispose();  // stops the engine view
    }
}
