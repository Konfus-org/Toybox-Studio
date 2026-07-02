using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Toybox.Studio.Events;
using Toybox.Studio.Utils;

namespace Toybox.Studio.AppHosting;

/// <summary>
/// Keeps watch over an owned app from both sides of the connection. While connected, it probes the app
/// with a bounded ping on a cadence, dispatching each round-trip as <see cref="AppPinged"/> and —
/// crucially — noticing when a still-connected app stops answering: a frozen peer keeps its socket open,
/// so neither a process exit nor an RPC disconnect fires; this loop is the only thing that catches that
/// case, dispatching <see cref="AppStoppedResponding"/> past the threshold and
/// <see cref="AppResumedResponding"/> on recovery (or once the connection is gone, so any open prompt
/// retracts). While disconnected, it watches the app's well-known port instead, dispatching
/// <see cref="AppInstanceDetected"/> for each sighting of an already-running instance (sightings repeat,
/// so a failed attach is naturally retried). What to do about either finding is the owner's call — this
/// class only observes; the owner can <see cref="Snooze"/> to be warned again after another full
/// threshold.
/// </summary>
public sealed class OwnedAppWatchdog : IDisposable
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(2);

    // Each ping is bounded so a frozen peer parks that probe, not the whole loop; a short retry pause
    // (instead of the full interval) crosses the unresponsive threshold promptly once pings start failing.
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PingRetryDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan UnresponsiveThreshold = TimeSpan.FromSeconds(5);

    // An instance probe is a bare TCP connect; anything listening answers well inside this.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(250);

    private readonly OwnedApp _app;
    private readonly string _pingMethod;
    private readonly int _instancePort;
    private readonly EventDispatcher _events;
    private CancellationTokenSource? _cts;

    // Shared between the loop and Snooze: the tick of the last good ping, and whether the app is
    // currently flagged unresponsive (0/1 for Interlocked).
    private long _lastPingOkTicks;
    private int _isUnresponsive;

    public OwnedAppWatchdog(OwnedApp app, string pingMethod, int instancePort, EventDispatcher events)
    {
        _app = app;
        _pingMethod = pingMethod;
        _instancePort = instancePort;
        _events = events;
    }

    public void Start()
    {
        if (_cts is not null)
            return;

        _cts = new CancellationTokenSource();
        Task.Run(() => RunAsync(_cts.Token)).FireAndForget();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// The owner chose to keep waiting on a frozen app: treat now as the last good moment so the
    /// watchdog re-arms and warns again only if the app stays frozen for another full threshold.
    /// </summary>
    public void Snooze()
    {
        Interlocked.Exchange(ref _lastPingOkTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _isUnresponsive, 0);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var wasConnected = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!_app.IsConnected)
                {
                    // The app going away also resolves "unresponsive" (retract any prompt). While
                    // disconnected, watch the well-known port for a running instance to attach to;
                    // probing pauses while connected so stray connects never disturb a live RPC session.
                    wasConnected = false;
                    MarkResponsive();
                    if (await ProbeInstanceAsync(ct).ContinueOnAnyContext())
                        _events.Dispatch(new AppInstanceDetected(_instancePort));
                    await Task.Delay(PingInterval, ct).ContinueOnAnyContext();
                    continue;
                }

                // A fresh connection measures the threshold from now, not from a stale last-good tick.
                if (!wasConnected)
                {
                    wasConnected = true;
                    Snooze();
                }

                var stopwatch = Stopwatch.StartNew();
                var ping = await _app.SendCommandAsync(_pingMethod, null, PingTimeout, ct).ContinueOnAnyContext();
                if (ct.IsCancellationRequested)
                    break;

                if (ping.Success)
                {
                    Interlocked.Exchange(ref _lastPingOkTicks, DateTime.UtcNow.Ticks);
                    _events.Dispatch(new AppPinged(stopwatch.Elapsed));
                    MarkResponsive();
                    await Task.Delay(PingInterval, ct).ContinueOnAnyContext();
                }
                else
                {
                    // No reply (timed out or errored) while still connected: the app may be frozen.
                    EvaluateResponsiveness();
                    await Task.Delay(PingRetryDelay, ct).ContinueOnAnyContext();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled on dispose.
        }
    }

    /// <summary>Flags the app unresponsive (once) past the threshold without a good ping.</summary>
    private void EvaluateResponsiveness()
    {
        if (!_app.IsConnected)
            return;

        var sinceLastOk = DateTime.UtcNow - new DateTime(Interlocked.Read(ref _lastPingOkTicks));
        if (sinceLastOk < UnresponsiveThreshold)
            return;

        if (Interlocked.Exchange(ref _isUnresponsive, 1) == 1)
            return;

        _events.Dispatch(new AppStoppedResponding(sinceLastOk));
    }

    /// <summary>Clears the unresponsive flag and announces recovery, if it was set.</summary>
    private void MarkResponsive()
    {
        if (Interlocked.Exchange(ref _isUnresponsive, 0) == 1)
            _events.Dispatch(new AppResumedResponding());
    }

    private async Task<bool> ProbeInstanceAsync(CancellationToken ct)
    {
        try
        {
            using var probe = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);
            await probe.ConnectAsync(IPAddress.Loopback, _instancePort, timeout.Token).ContinueOnAnyContext();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
