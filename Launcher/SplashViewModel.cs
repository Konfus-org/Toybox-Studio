using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Shell;

/// <summary>
/// Backs the splash screen with a loading bar and a fun, toy-themed line per startup phase, derived
/// from the engine's dispatched <see cref="EngineStateChanged"/>. The real diagnostics keep landing in
/// the unified log (TbxStudio.log and the main window's console); the splash just keeps the wait playful.
/// </summary>
public sealed partial class SplashViewModel : ObservableEventSubscriber, IEventHandler<EngineStateChanged>
{
    // One pool of lines per startup phase; a random one is picked as the phase is entered.
    private static readonly string[] SearchingLines =
    [
        "Rummaging through the toybox…",
        "Looking for the instruction manual…",
        "Untangling the string lights…",
    ];

    private static readonly string[] CompilingLines =
    [
        "Molding fresh plastic bricks…",
        "Snapping bricks together…",
        "Painting the minifigs…",
    ];

    private static readonly string[] EngineStartLines =
    [
        "Inserting fresh batteries…",
        "Winding up the engine…",
        "Pulling the rip cord…",
    ];

    private static readonly string[] WorldLines =
    [
        "Unboxing the world…",
        "Setting up the play mat…",
        "Fluffing the clouds…",
    ];

    // Which Loading sub-step we're on: the engine reports Loading twice at startup (launching, then
    // preparing the world once connected); count the transitions instead of coupling to its text.
    private int _loadingSteps;

    public SplashViewModel(EventDispatcher events) : base(events)
    {
    }

    /// <summary>The playful phase line (or a plain failure message set by the startup flow).</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = Pick(SearchingLines);

    /// <summary>Startup progress in [0,1], driving the loading bar. Only ever moves forward.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; } = 0.05;

    public void Handle(in EngineStateChanged evt)
    {
        var state = evt.State;
        Dispatch.To(DispatchContext.UI, () => Apply(state));
    }

    private void Apply(EngineState state)
    {
        var (message, progress) = state.Phase switch
        {
            EnginePhase.Compiling => (Pick(CompilingLines), 0.35),
            EnginePhase.Loading when _loadingSteps == 0 => (Pick(EngineStartLines), 0.6),
            EnginePhase.Loading => (Pick(WorldLines), 0.85),
            EnginePhase.Ready or EnginePhase.Playing => ("Ready to play!", 1.0),
            _ => (Status, Progress), // Off (e.g. a failed launch): the startup flow owns the message.
        };

        if (state.Phase == EnginePhase.Loading)
            _loadingSteps++;

        // The bar only ratchets forward, so a transient state can never make startup look like it rewound.
        if (progress < Progress)
            return;

        Status = message;
        Progress = progress;
    }

    private static string Pick(string[] lines) => lines[Random.Shared.Next(lines.Length)];
}
