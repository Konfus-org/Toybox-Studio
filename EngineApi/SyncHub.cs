using Newtonsoft.Json.Linq;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// The connection point between every bound <see cref="EngineObject"/> and the engine: it owns the
/// outbound <see cref="SyncScheduler"/> and routes inbound <c>sync.changed</c> notifications (dispatched
/// by <see cref="Engine"/> as <see cref="SyncChanged"/> events) to the object registered under the
/// matching <see cref="EngineAddress"/>, applying on the UI thread. One instance, created by the
/// composition root; objects attach through <see cref="EngineObject.Bind"/>, never directly here.
/// </summary>
public sealed class SyncHub : EventSubscriber, IEventHandler<SyncChanged>
{
    private readonly Logger _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, EngineObject> _bound = [];

    public SyncHub(Engine engine, Logger log, EventDispatcher events) : base(events)
    {
        Engine = engine;
        _log = log;
        Scheduler = new SyncScheduler(engine, log);
    }

    /// <summary>The engine connection bound objects send their commands through.</summary>
    public Engine Engine { get; }

    /// <summary>The outbound edit timing (Live/Batched/Manual), shared by every bound object.</summary>
    internal SyncScheduler Scheduler { get; }

    public void Handle(in SyncChanged evt)
    {
        var (address, key, value) = evt;
        Dispatch.To(DispatchContext.UI, () => Route(address, key, value));
    }

    internal void Register(EngineObject obj)
    {
        var address = obj.AddressKey;
        lock (_gate)
        {
            if (_bound.TryGetValue(address, out var existing) && !ReferenceEquals(existing, obj))
                _log.Warning($"Engine sync address '{address}' was already bound; the newer object replaces it.");
            _bound[address] = obj;
        }
    }

    internal void Unregister(EngineObject obj)
    {
        lock (_gate)
        {
            // Look the object up by identity, not by its current Address — the address may have changed
            // since it bound, and unbinding must always detach exactly this object.
            foreach (var (address, bound) in _bound)
            {
                if (!ReferenceEquals(bound, obj))
                    continue;

                _bound.Remove(address);
                break;
            }
        }

        Scheduler.Drop(obj);
    }

    private void Route(string address, string key, JToken value)
    {
        EngineObject? bound;
        lock (_gate)
            _bound.TryGetValue(address, out bound);

        // A change for an unbound address is normal — the editor simply isn't mirroring it right now.
        bound?.ApplyFromEngine(key, value);
    }
}
