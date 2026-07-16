using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Hosting;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Projects;

/// <summary>
/// Coordinates the engine's host with everything it deliberately doesn't know about: the active project
/// supplies the launcher (built first, through the <see cref="ProjectBuilder"/>), a detected running
/// instance means attach, and an unresponsive engine is reported and waited out (a real confirm dialog
/// can offer a force-restart once the editor grows a dialog layer again). The host itself only
/// launches/attaches/stops what it is handed.
/// </summary>
public sealed class EngineCoordinator :
    EventSubscriber,
    IEventHandler<AppInstanceDetected>,
    IEventHandler<AppStoppedResponding>,
    IEventHandler<AppResumedResponding>,
    IEventHandler<EngineLaunchRequested>
{
    private readonly Project _project;
    private readonly ProjectBuilder _builder;
    private readonly AppHost<Engine> _host;
    private readonly OwnedAppWatchdog _ownedAppWatchdog;
    private readonly bool _hideEngineWindow;
    private readonly int _connectTimeoutSeconds;
    private readonly Logger _log;

    // One build-and-launch at a time: the startup/switch callers never overlap, but a viewport's "Launch
    // Engine" call-to-action can be clicked repeatedly before the state leaves Off, so a second request is
    // turned away here rather than racing a concurrent build.
    private readonly ReentrancyGuard _launchGate = new();

    // Cancelled on dispose so an in-flight build/launch dies with the app instead of orphaning CMake/Ninja.
    private readonly CancellationTokenSource _lifetime = new();

    public EngineCoordinator(
        Project project,
        ProjectBuilder builder,
        AppHost<Engine> host,
        OwnedAppWatchdog ownedAppWatchdog,
        bool hideEngineWindow,
        int connectTimeoutSeconds,
        Logger log,
        EventDispatcher events)
        : base(events)
    {
        _project = project;
        _builder = builder;
        _host = host;
        _ownedAppWatchdog = ownedAppWatchdog;
        _hideEngineWindow = hideEngineWindow;
        _connectTimeoutSeconds = connectTimeoutSeconds;
        _log = log;
    }

    /// <summary>
    /// Builds the active project and launches its engine. A failed build no-ops (logged); launch/connect
    /// failures land in the log too.
    /// </summary>
    public async Task StartEngineAsync(CancellationToken ct = default)
    {
        // Bail if a launch is already underway (e.g. a rapid second click on the viewport call-to-action,
        // before the compile flips the engine out of its Off state and retracts the prompt).
        using var launchScope = _launchGate.TryEnter();
        if (launchScope is null)
            return;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        // A Debug Studio drives a Debug engine, a Release Studio a Release engine.
        var build = await _builder.BuildAsync(_project, StudioBuild.Mode, linked.Token).ContinueOnAnyContext();
        if (!build)
        {
            _log.Error(build.Error!);
            return;
        }

        var appSettings = ProjectLoader.SettingsPathFor(_project.Path);
        var launch = new EngineLaunchInfo(build.Value!, _project.Module, appSettings, _hideEngineWindow)
        {
            ConnectTimeoutSeconds = _connectTimeoutSeconds,
            AssetRoot = _project.Path,
        };
        await _host.StartAsync(launch, linked.Token).ContinueOnAnyContext();
    }

    /// <summary>An already-running engine appeared: attach to it instead of launching our own. A no-op
    /// while a session is already starting or live; the sighting repeats, so a failed attach retries.</summary>
    public void Handle(in AppInstanceDetected evt) => _host.AttachAsync(evt.Port).FireAndForget();

    /// <summary>A viewport's "Launch Engine" call-to-action was invoked while the engine was off: build the
    /// active project and launch its engine, the same path the startup and project-switch flows take.</summary>
    public void Handle(in EngineLaunchRequested evt) => StartEngineAsync().FireAndForget();

    // There is no dialog layer to ask the user yet, so a frozen engine is logged and waited out; the
    // snooze re-arms the watchdog to warn again if it stays frozen for another full threshold.
    public void Handle(in AppStoppedResponding evt)
    {
        _log.Warning(
            $"Engine has not responded for {evt.Silence.TotalSeconds:F0}s; it may be frozen. "
                + "Waiting for it to recover...");
        _ownedAppWatchdog.Snooze();
    }

    public void Handle(in AppResumedResponding evt)
    {
        // Only announce a genuine recovery — the watchdog also dispatches this when a flagged connection
        // goes away entirely (so any future prompt retracts), where "resumed responding" would mislead.
        if (_host.State == ConnectionState.Connected)
            _log.Info("Engine resumed responding.");
    }

    public override void Dispose()
    {
        base.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
