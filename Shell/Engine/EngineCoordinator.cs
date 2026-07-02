using Toybox.Studio.AppHosting;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Shell;

/// <summary>
/// Coordinates the engine's host with everything it deliberately doesn't know about: the project
/// supplies the launcher (built first), a detected running instance means attach, a project change
/// means restart onto the new project's build, and an unresponsive engine is reported and waited out
/// (a real confirm dialog can offer a force-restart once the editor grows a dialog layer again).
/// The host itself only launches/attaches/stops what it is handed.
/// </summary>
public sealed class EngineCoordinator :
    EventSubscriber,
    IEventHandler<ProjectChanged>,
    IEventHandler<AppInstanceDetected>,
    IEventHandler<AppStoppedResponding>,
    IEventHandler<AppResumedResponding>
{
    private readonly ExampleProject _project;
    private readonly AppHost<Engine> _host;
    private readonly OwnedAppWatchdog _ownedAppWatchdog;
    private readonly StudioSettings _settings;
    private readonly Logger _log;

    // Cancelled on dispose so an in-flight build/launch dies with the app instead of orphaning CMake/Ninja.
    private readonly CancellationTokenSource _lifetime = new();

    // Coalesces rapid project changes: only the latest pending switch is honoured.
    private int _pendingProjectChange;

    public EngineCoordinator(
        ExampleProject project,
        AppHost<Engine> host,
        OwnedAppWatchdog ownedAppWatchdog,
        StudioSettings settings,
        Logger log,
        EventDispatcher events)
        : base(events)
    {
        _project = project;
        _host = host;
        _ownedAppWatchdog = ownedAppWatchdog;
        _settings = settings;
        _log = log;
    }

    /// <summary>
    /// Builds the current project and launches its engine — the startup path and the project-change
    /// restart path. No-ops (logged) when no project is open or the build fails.
    /// </summary>
    public async Task StartEngineAsync(CancellationToken ct = default)
    {
        if (_project.Current is not { } project)
        {
            _log.Error("Open a project to launch.");
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        var launcher = await _project.PrepareLauncherAsync(linked.Token).ContinueOnAnyContext();
        if (launcher is null)
            return;

        var launch = new EngineLaunchInfo(
            launcher, project.ModuleName, project.AppSettingsPath, _settings.HideEngineWindow)
        {
            ConnectTimeoutSeconds = _settings.ConnectTimeoutSeconds,
        };
        await _host.StartAsync(launch, linked.Token).ContinueOnAnyContext();
    }

    // A project switch relaunches the engine so its world matches the new project. The very first launch
    // is driven by App startup; this only reacts to later changes while an engine is already live.
    public void Handle(in ProjectChanged evt)
    {
        if (_project.Current is null || _host.State == ConnectionState.Disconnected)
            return;

        // Coalesce rapid changes: an in-flight restart re-runs once with the latest project instead of
        // queuing overlapping stop/build/launch sequences that fight over the session.
        if (Interlocked.Exchange(ref _pendingProjectChange, 1) == 1)
            return;

        RestartLoopAsync().FireAndForget();
    }

    /// <summary>An already-running engine appeared: attach to it instead of launching our own. A no-op
    /// while a session is already starting or live; the sighting repeats, so a failed attach retries.</summary>
    public void Handle(in AppInstanceDetected evt) => _host.AttachAsync(evt.Port).FireAndForget();

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

    // Drains coalesced project changes: restarts once per latch, re-running while another change landed
    // during the restart so the engine always ends up on the most recently selected project.
    private async Task RestartLoopAsync()
    {
        while (Interlocked.Exchange(ref _pendingProjectChange, 0) == 1)
        {
            // Stop first: the engine binaries are built in-tree, so a running engine would hold the very
            // files the rebuild is about to relink.
            await _host.StopAsync().ContinueOnAnyContext();
            await StartEngineAsync().ContinueOnAnyContext();
        }
    }
}
