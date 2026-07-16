using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.LogConsole;
using Toybox.Studio.Projects;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio;

/// <summary>
/// Backs the splash screen with a loading bar and a fun, toy-themed line per startup phase, derived
/// from the engine's dispatched <see cref="EngineStateChanged"/>. The real diagnostics keep landing in
/// the unified log (TbxStudio.log and the main window's console); the splash just keeps the wait playful —
/// except in a developer (Debug) build, where it also carries a live log console so a long compile or
/// download is legible while it runs.
/// </summary>
public sealed partial class SplashViewModel : ObservableEventSubscriber,
    IEventHandler<EngineStateChanged>,
    IEventHandler<LaunchActivityChanged>,
    IEventHandler<LaunchActivityProgress>
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

    public SplashViewModel(EventDispatcher events, ViewModelFactory viewModels) : base(events)
    {
        // The base line starts on the same playful pick the Status property was initialized with (an
        // initializer can't cross-reference it, so mirror it here once construction has run).
        _baseStatus = Status;
#if DEBUG
        // Developer build: surface the live log stream on the splash so a long compile/download is legible
        // as it happens. Reuses the same log console the main window's dockable uses. Release builds keep
        // the splash clean and never construct it (so nothing subscribes to the log here).
        LogConsole = viewModels.Create<LogConsoleViewModel>();
        ShowLogConsole = true;
#endif
    }

    /// <summary>The live log console shown on the splash in developer builds — null in Release. Reuses the
    /// same <see cref="LogConsoleViewModel"/> as the main window's Log Console, fed by the unified log.</summary>
    public LogConsoleViewModel? LogConsole { get; }

    /// <summary>Whether the splash shows its log console — on in Debug builds, off (and absent) in Release.</summary>
    public bool ShowLogConsole { get; }

    /// <summary>The single status line shown on the splash: a compile/git activity caption while one is
    /// running (it takes precedence), otherwise the base line — the playful phase message or a plain
    /// launch announcement. VM-owned; the launch flow sets its plain messages via <see cref="Announce"/>.</summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = Pick(SearchingLines);

    // The base status line to fall back to when no activity caption is taking precedence: the latest
    // playful phase message, or a plain launch announcement. Restored to Status when an activity ends.
    private string _baseStatus;

    /// <summary>Startup progress in [0,1], driving the loading bar. Only ever moves forward.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; } = 0.05;

    /// <summary>The motion state the splash icon plays: rocking while loading, a single nod once ready.</summary>
    [ObservableProperty]
    public partial SplashPhase Phase { get; private set; } = SplashPhase.Loading;

    // The launch activity the secondary bar is currently narrating (a compile, a git fetch), or null when
    // none is running. Tracked so a late "finished" for one activity can't tear down another's bar.
    private LaunchActivity? _activity;

    /// <summary>Whether a secondary launch activity (a compile or a git operation) is underway — shows the
    /// dedicated activity bar under the main one, since these are the longest, least-legible parts of a
    /// launch and deserve their own captioned progress.</summary>
    [ObservableProperty]
    public partial bool IsActivityRunning { get; private set; }

    /// <summary>The current activity's progress in [0,1], driving the second bar while
    /// <see cref="IsActivityRunning"/>. Only advances within an activity (a tool can re-report a lower
    /// count across retries); it resets as each new activity begins.</summary>
    [ObservableProperty]
    public partial double ActivityProgress { get; private set; }

    /// <summary>Whether the activity bar runs indeterminate — true until the running tool reports its first
    /// measurable step, and throughout phases that report none (a cmake configure, an MSVC build, git's
    /// object-counting phases).</summary>
    [ObservableProperty]
    public partial bool ActivityIsIndeterminate { get; private set; } = true;

    /// <summary>Set once the studio window has taken over; the splash window fades down and closes on it.</summary>
    [ObservableProperty]
    public partial bool IsDismissed { get; private set; }

    /// <summary>The icon on the box: the picked project's own icon, or the Toybox logo until one is chosen.</summary>
    [ObservableProperty]
    public partial Bitmap Icon { get; private set; } = new(AssetLoader.Open(FallbackIcon));

    /// <summary>Sets the base status line to a plain launch message (a failure, a "no engine" notice). Shown
    /// on the single status line unless a compile/git activity caption is currently taking precedence.</summary>
    public void Announce(string message)
    {
        _baseStatus = message;
        if (_activity is null)
            Status = message;
    }

    /// <summary>Swaps the splash icon to the picked project's (keeping the Toybox logo when it has none).</summary>
    public void ShowProject(Project project)
    {
        if (project.Icon is { } icon)
            Icon = icon;
    }

    /// <summary>
    /// Marks loading finished so the icon takes its Ready bow (the nod) and the bar fills the rest of the
    /// way. The launch flow calls this at handoff because the engine's own Ready state (which is what
    /// otherwise fills the bar to 1.0) usually lands only after the studio window has taken over — the
    /// world keeps loading behind it — too late for the splash to react to, which would otherwise leave the
    /// bar resting a step short of full at handoff.
    /// </summary>
    public void FinishLoading()
    {
        Progress = 1.0;
        Phase = SplashPhase.Ready;
    }

    /// <summary>Starts the fade-down handoff to the studio window (the window closes itself when it ends).</summary>
    public void Dismiss() => IsDismissed = true;

    /// <summary>Unsubscribes the splash's handlers and, in a developer build, the log console it owns (so
    /// its subscription to the log stream doesn't outlive the splash).</summary>
    public override void Dispose()
    {
        LogConsole?.Dispose();
        base.Dispose();
    }

    public void Handle(in EngineStateChanged evt)
    {
        var state = evt.State;
        Dispatch.To(DispatchContext.UI, () => Apply(state));
    }

    public void Handle(in LaunchActivityChanged evt)
    {
        var (activity, active) = (evt.Activity, evt.Active);
        Dispatch.To(DispatchContext.UI, () =>
        {
            if (active)
            {
                // A fresh activity takes over: caption the single status line (this fact wins over the
                // playful base line) and start its bar empty and indeterminate (its first reported step
                // turns it determinate — see the progress handler).
                _activity = activity;
                Status = CaptionFor(activity);
                ActivityProgress = 0;
                ActivityIsIndeterminate = true;
                IsActivityRunning = true;
            }
            else if (_activity == activity)
            {
                // Only the activity that owns the bar can end it — a stray late "finished" for an earlier
                // one can't tear down whatever is running now (activities run one at a time anyway). The
                // status line falls back to the base line (the latest playful phase message).
                _activity = null;
                IsActivityRunning = false;
                Status = _baseStatus;
            }
        });
    }

    public void Handle(in LaunchActivityProgress evt)
    {
        var fraction = evt.Fraction;
        Dispatch.To(DispatchContext.UI, () =>
        {
            ActivityIsIndeterminate = false;

            // Only ever fill forward, so a tool re-reporting a lower count (a build retry after a transient
            // file lock, a git phase that restarts its percentage) can't make the bar look like it rewound.
            if (fraction > ActivityProgress)
                ActivityProgress = fraction;
        });
    }

    private void Apply(EngineState state)
    {
        var (message, progress) = state.Phase switch
        {
            EnginePhase.Compiling => (Pick(CompilingLines), 0.35),
            EnginePhase.Loading when _loadingSteps == 0 => (Pick(EngineStartLines), 0.6),
            EnginePhase.Loading => (Pick(WorldLines), 0.85),
            EnginePhase.Ready or EnginePhase.Playing => ("Ready to play!", 1.0),
            _ => (_baseStatus, Progress), // Off (e.g. a failed launch): the startup flow owns the message.
        };

        if (state.Phase == EnginePhase.Loading)
            _loadingSteps++;

        // The bar only ratchets forward, so a transient state can never make startup look like it rewound.
        if (progress < Progress)
            return;

        // Record the playful line as the base, but don't stomp a compile/git caption that's currently
        // taking precedence on the single status line — it'll surface when the activity ends.
        _baseStatus = message;
        if (_activity is null)
            Status = message;
        Progress = progress;
        if (state.Phase is EnginePhase.Ready or EnginePhase.Playing)
            Phase = SplashPhase.Ready;
    }

    private static string Pick(string[] lines) => lines[Random.Shared.Next(lines.Length)];

    // The activity bar's caption: obvious about what's happening (compiling the engine, or a git transfer)
    // with a light toy-box lilt, so the wait reads clearly without turning stiff. The playful phase lines
    // above (Status) carry the flavor; this line carries the plain fact.
    private static string CaptionFor(LaunchActivity activity) => activity switch
    {
        LaunchActivity.Compiling => "Compiling the engine…",
        LaunchActivity.Downloading => "Fetching the engine from Git…",
        LaunchActivity.Updating => "Pulling updates from Git…",
        _ => "",
    };
}
