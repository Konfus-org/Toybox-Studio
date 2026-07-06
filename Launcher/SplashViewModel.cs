using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Projects;
using Toybox.Studio.Utils;

namespace Toybox.Studio;

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

    // The icon shown until a project (with an icon of its own) is picked.
    private static readonly Uri FallbackIcon = new("avares://Toybox.Studio.Resources/Icons/Toybox.png");

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

    /// <summary>The motion state the splash icon plays: rocking while loading, a single nod once ready.</summary>
    [ObservableProperty]
    public partial SplashPhase Phase { get; private set; } = SplashPhase.Loading;

    /// <summary>Set once the studio window has taken over; the splash window fades down and closes on it.</summary>
    [ObservableProperty]
    public partial bool IsDismissed { get; private set; }

    /// <summary>The icon on the box: the picked project's own icon, or the Toybox logo until one is chosen.</summary>
    [ObservableProperty]
    public partial Bitmap Icon { get; private set; } = new(AssetLoader.Open(FallbackIcon));

    /// <summary>Swaps the splash icon to the picked project's (keeping the Toybox logo when it has none).</summary>
    public void ShowProject(Project project)
    {
        if (project.Icon is { } icon)
            Icon = icon;
    }

    /// <summary>
    /// Marks loading finished so the icon takes its Ready bow (the nod). The launch flow calls this at
    /// handoff because the engine's own Ready state usually lands only after the studio window has taken
    /// over (the world keeps loading behind it) — too late for the splash to react to.
    /// </summary>
    public void FinishLoading() => Phase = SplashPhase.Ready;

    /// <summary>Starts the fade-down handoff to the studio window (the window closes itself when it ends).</summary>
    public void Dismiss() => IsDismissed = true;

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
        if (state.Phase is EnginePhase.Ready or EnginePhase.Playing)
            Phase = SplashPhase.Ready;
    }

    private static string Pick(string[] lines) => lines[Random.Shared.Next(lines.Length)];
}
