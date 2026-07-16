using Newtonsoft.Json.Linq;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// Applies each <see cref="SyncMode"/>'s outbound timing. <see cref="SyncMode.TwoWay"/> sends at once.
/// <see cref="SyncMode.Batched"/> sends the leading edge immediately, then coalesces follow-ups into
/// one trailing send per batch frequency (latest value wins), so a gizmo drag feels live without
/// flooding the wire. <see cref="SyncMode.Manual"/> stages until the owner's flush. Sends are
/// fire-and-forget commands whose failures land in the log — a lost sync never blocks the editor.
/// </summary>
public sealed class SyncScheduler
{
    // The fastest batch frequency; also the flood ceiling for un-tuned Batched properties (~30Hz).
    private const int MinBatchFrequencyMs = 33;

    private readonly Engine _engine;
    private readonly Logger _log;
    private readonly object _gate = new();

    // Batched pushes waiting on their trailing timer plus Manual staged edits, latest payload per key.
    private readonly Dictionary<PendingKey, Pending> _pending = [];

    // When each Batched key last actually sent, for the leading-edge test.
    private readonly Dictionary<PendingKey, long> _lastSent = [];

    internal SyncScheduler(Engine engine, Logger log)
    {
        _engine = engine;
        _log = log;
    }

    /// <summary>Queues one outbound edit per the slot's mode; the payload is complete and sends as-is.</summary>
    internal void Schedule(EngineObject owner, SyncSlot slot, JObject payload)
    {
        switch (slot.Mode)
        {
            case SyncMode.TwoWay:
            case SyncMode.OneWayFromStudio:
                Send(slot, payload);
                break;
            case SyncMode.Batched:
                Throttle(new PendingKey(owner, slot.Key), slot, payload);
                break;
            case SyncMode.Manual:
                Stage(new PendingKey(owner, slot.Key), slot, payload);
                break;
        }
    }

    /// <summary>Sends everything staged for <paramref name="owner"/> — manual edits and batched pushes
    /// still waiting on their window — reporting the first failure (but attempting every send).</summary>
    internal async Task<Result> FlushAsync(EngineObject owner, CancellationToken ct)
    {
        List<Pending> staged;
        lock (_gate)
            staged = TakePending(owner);

        string? firstError = null;
        foreach (var pending in staged)
        {
            var result = await _engine.SendCommandAsync(pending.Slot.Command, pending.Payload, ct)
                .ContinueOnAnyContext();
            firstError ??= result ? null : result.Error;
        }

        return firstError is null ? Result.Ok() : Result.Fail(firstError);
    }

    /// <summary>Drops everything scheduled for <paramref name="owner"/> (it is unbinding).</summary>
    internal void Drop(EngineObject owner)
    {
        lock (_gate)
        {
            TakePending(owner);
            foreach (var key in _lastSent.Keys.Where(key => ReferenceEquals(key.Owner, owner)).ToList())
                _lastSent.Remove(key);
        }
    }

    private void Throttle(PendingKey key, SyncSlot slot, JObject payload)
    {
        var frequency = Math.Max(slot.BatchFrequencyMs, MinBatchFrequencyMs);
        lock (_gate)
        {
            // A trailing send is already scheduled: the newest value just replaces its payload.
            if (_pending.TryGetValue(key, out var pending))
            {
                pending.Payload = payload;
                return;
            }

            var now = Environment.TickCount64;
            if (_lastSent.TryGetValue(key, out var last) && now - last < frequency)
            {
                // Inside the batch: stage and let the trailing timer send the final value.
                var due = Math.Max(1, (int)(frequency - (now - last)));
                var trailing = new Pending(slot, payload);
                trailing.Timer = new Timer(_ => FireTrailing(key), null, due, Timeout.Infinite);
                _pending[key] = trailing;
                return;
            }

            _lastSent[key] = now;
        }

        // Leading edge: the first edit in a while goes out immediately, so the change feels live.
        Send(slot, payload);
    }

    private void FireTrailing(PendingKey key)
    {
        Pending? trailing;
        lock (_gate)
        {
            if (!_pending.Remove(key, out trailing))
                return;

            trailing.Timer?.Dispose();
            _lastSent[key] = Environment.TickCount64;
        }

        Send(trailing.Slot, trailing.Payload);
    }

    private void Stage(PendingKey key, SyncSlot slot, JObject payload)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(key, out var staged))
                staged.Payload = payload;
            else
                _pending[key] = new Pending(slot, payload);
        }
    }

    // Removes and returns everything pending for the owner, cancelling any trailing timers. Callers
    // hold the gate.
    private List<Pending> TakePending(EngineObject owner)
    {
        var taken = new List<Pending>();
        foreach (var (key, pending) in _pending.Where(entry => ReferenceEquals(entry.Key.Owner, owner)).ToList())
        {
            pending.Timer?.Dispose();
            _pending.Remove(key);
            taken.Add(pending);
        }

        return taken;
    }

    private void Send(SyncSlot slot, JObject payload) => SendAsync(slot, payload).FireAndForget();

    private async Task SendAsync(SyncSlot slot, JObject payload)
    {
        var result = await _engine.SendCommandAsync(slot.Command, payload, CancellationToken.None)
            .ContinueOnAnyContext();
        if (!result)
            _log.Warning($"Engine sync '{slot.Command}' for '{slot.Key}' failed: {result.Error}");
    }

    private readonly record struct PendingKey(EngineObject Owner, string Key);

    private sealed class Pending(SyncSlot slot, JObject payload)
    {
        public SyncSlot Slot { get; } = slot;
        public JObject Payload { get; set; } = payload;
        public Timer? Timer { get; set; }
    }
}
