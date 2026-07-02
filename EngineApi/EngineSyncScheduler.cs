using System;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// The single channel reflected-object edits flow through on their way to the engine. It applies each field's
/// <see cref="EngineSyncMode"/>: <see cref="EngineSyncMode.OnChanged"/> throttles — the first change pushes immediately, then
/// rapid follow-ups coalesce to at most one push per enforced window (leading + trailing) so a live scrub doesn't
/// flood the channel (fire-and-forget, failures surfaced on <see cref="PushFailed"/>); <see cref="EngineSyncMode.Timer"/>
/// debounces on a per-field trailing edge; <see cref="EngineSyncMode.Manual"/> stages the latest value until
/// <see cref="FlushAsync"/>. Every push
/// bottoms out in the uniform <c>reflect.*</c> verbs against a <see cref="EngineAddress"/> — the wire method name
/// lives here, in exactly one place, for every tier. A single editor instance, injected by the composition root.
/// </summary>
public sealed class EngineSyncScheduler(Engine engine) : IEngineSyncScheduler
{
    private readonly Engine _engine = engine;
    private readonly object _gate = new();
    private readonly Dictionary<Key, JToken> _staged = [];
    private readonly Dictionary<Key, Timer> _timers = [];

    // Last time each OnChanged field actually hit the wire — the throttle's leading-edge gate.
    private readonly Dictionary<Key, long> _lastPush = [];

    // The enforced floor on how often an OnChanged ("continuous") field syncs. A live scrub/drag can fire the setter
    // every UI frame or faster; uncapped, each change is its own reflect.set and floods the channel. So OnChanged
    // THROTTLES (not debounces — it must keep updating DURING the gesture, not only once it settles): the first
    // change pushes at once, then follow-ups coalesce to one push per window with a trailing push for the final
    // value. A field may ask to sync slower via [EngineSync(windowMs:)]; it can never sync faster than this floor.
    private const int MinThrottleMs = 33;

    /// <summary>Raised (on a background thread) when an immediate <see cref="EngineSyncMode.OnChanged"/> push
    /// fails — the editor's optimistic edits don't await, so this is how a rejection surfaces.</summary>
    public event Action<string>? PushFailed;

    public void Push(EngineAddress address, string wire, JToken value, EngineSyncMode mode, int windowMs)
    {
        var key = new Key(address, wire);
        switch (mode)
        {
            case EngineSyncMode.OnChanged:
                Throttle(key, address, wire, value, windowMs);
                break;
            case EngineSyncMode.Manual:
                lock (_gate)
                    _staged[key] = value;
                break;
            case EngineSyncMode.Timer:
                ScheduleDebounced(key, address, wire, value, windowMs);
                break;
        }
    }

    public async Task<Result> FlushAsync(EngineAddress address, CancellationToken ct = default)
    {
        List<(string Wire, JToken Value)> batch;
        lock (_gate)
        {
            batch = _staged
                .Where(entry => entry.Key.Address == address)
                .Select(entry => (entry.Key.Wire, entry.Value))
                .ToList();
            foreach (var (wire, _) in batch)
            {
                var key = new Key(address, wire);
                _staged.Remove(key);
                if (_timers.Remove(key, out var timer))
                    timer.Dispose();
            }
        }

        foreach (var (wire, value) in batch)
        {
            var result = await SetAsync(address, wire, value, ct).ContinueOnAnyContext();
            if (!result.Success)
                return result;
        }

        return Result.Ok();
    }

    public Task<Result> SetAsync(
        EngineAddress address, string wire, JToken value, CancellationToken ct = default) =>
        _engine.SendCommand(EngineMethods.SyncSet, new { Path = address.Field(wire).Path, Value = value }, ct);

    public Task<Result> ResetAsync(EngineAddress address, string wire, CancellationToken ct = default) =>
        _engine.SendCommand(EngineMethods.SyncReset, new { Path = address.Field(wire).Path }, ct);

    public async Task<Result<bool>> IsDefaultAsync(
        EngineAddress address, string wire, CancellationToken ct = default)
    {
        var result = await _engine.SendCommand<JObject>(
            EngineMethods.SyncIsDefault, new { Path = address.Field(wire).Path }, ct).ContinueOnAnyContext();
        return result is { Success: true, Value: { } reply }
            ? Result<bool>.Ok(reply.Value<bool?>("isDefault") ?? false)
            : Result<bool>.Fail(result.Error ?? "The engine returned no result.");
    }

    public async Task<Result<JObject>> DescribeAsync(EngineAddress address, CancellationToken ct = default)
    {
        var result = await _engine.SendCommand<JObject>(
            EngineMethods.SyncDescribe, new { address.Path }, ct).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } reply })
            return Result<JObject>.Fail(result.Error ?? "The engine returned no data.");

        return (reply["body"] ?? reply) is JObject body
            ? Result<JObject>.Ok(body)
            : Result<JObject>.Fail("The engine returned no body.");
    }

    // OnChanged throttle: push the first change immediately (responsive), then coalesce rapid follow-ups to one push
    // per window, with a trailing push so the final value always lands. The window is the field's own, floored at
    // MinThrottleMs — a "continuous" field can request a slower sync but never a faster one than the enforced floor.
    private void Throttle(Key key, EngineAddress address, string wire, JToken value, int windowMs)
    {
        var window = Math.Max(windowMs, MinThrottleMs);
        var leading = false;
        lock (_gate)
        {
            var now = Environment.TickCount64;
            var hasLast = _lastPush.TryGetValue(key, out var last);

            // Leading edge: fire at once when the field is idle — past the window with nothing already pending.
            if (!_timers.ContainsKey(key) && (!hasLast || now - last >= window))
            {
                _lastPush[key] = now;
                leading = true;
            }
            else
            {
                // Inside the window: keep only the latest value and arm one trailing push for the window's end.
                _staged[key] = value;
                if (!_timers.ContainsKey(key))
                {
                    var elapsed = hasLast ? now - last : 0;
                    _timers[key] = new Timer(
                        _ => FireThrottled(key, address, wire), null, Math.Max(1, window - elapsed), Timeout.Infinite);
                }
            }
        }

        if (leading)
            PushNow(address, wire, value);
    }

    // The throttle's trailing edge: push the value staged during the window and re-stamp the last-push time.
    private void FireThrottled(Key key, EngineAddress address, string wire)
    {
        JToken? value;
        lock (_gate)
        {
            if (_timers.Remove(key, out var timer))
                timer.Dispose();
            if (!_staged.Remove(key, out value))
                return;
            _lastPush[key] = Environment.TickCount64;
        }

        PushNow(address, wire, value);
    }

    private void ScheduleDebounced(Key key, EngineAddress address, string wire, JToken value, int windowMs)
    {
        lock (_gate)
        {
            _staged[key] = value;
            if (_timers.Remove(key, out var existing))
                existing.Dispose();
            _timers[key] = new Timer(
                _ => FireDebounced(key, address, wire), null, Math.Max(1, windowMs), Timeout.Infinite);
        }
    }

    private void FireDebounced(Key key, EngineAddress address, string wire)
    {
        JToken? value;
        lock (_gate)
        {
            if (_timers.Remove(key, out var timer))
                timer.Dispose();
            if (!_staged.Remove(key, out value))
                return;
        }

        PushNow(address, wire, value);
    }

    private void PushNow(EngineAddress address, string wire, JToken value) =>
        PushAsync(address, wire, value).FireAndForget();

    private async Task PushAsync(EngineAddress address, string wire, JToken value)
    {
        var result = await SetAsync(address, wire, value, CancellationToken.None).ContinueOnAnyContext();
        if (!result.Success)
            PushFailed?.Invoke(result.Error ?? $"Failed to sync '{wire}'.");
    }

    // Identifies one field on one bound object. EngineAddress is a value type, so two handles to the same object
    // share a debounce bucket — coalescing rapid edits to the same field, which is what we want.
    private readonly record struct Key(EngineAddress Address, string Wire);
}
