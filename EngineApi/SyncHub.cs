using Newtonsoft.Json.Linq;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// The connection point between every bound <see cref="EngineObject"/> and the engine: it owns the
/// outbound <see cref="SyncScheduler"/> and routes inbound <c>sync.changed</c> / <c>sync.event</c>
/// notifications (dispatched by <see cref="Engine"/> as <see cref="SyncChanged"/> /
/// <see cref="SyncEventRaised"/> events) to the object registered under the matching
/// <see cref="EngineAddress"/>, applying and raising on the UI thread. One instance, created by the
/// composition root; objects attach through <see cref="EngineObject.Bind"/>, never directly here.
/// </summary>
public sealed class SyncHub : EventSubscriber, IEventHandler<SyncChanged>, IEventHandler<SyncEventRaised>
{
    private readonly object _gate = new();
    private readonly Dictionary<string, EngineObject> _bound = [];

    public SyncHub(Engine engine, Logger log, EventDispatcher events) : base(events)
    {
        Engine = engine;
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

    public void Handle(in SyncEventRaised evt)
    {
        var (address, key, args) = evt;
        Dispatch.To(DispatchContext.UI, () => RouteEvent(address, key, args));
    }

    internal void Register(EngineObject obj)
    {
        // Engine-global state (EngineAddress.None) has no inbound identity — nothing routes to it, so
        // it binds for its outbound commands only.
        var address = obj.AddressKey;
        if (address.Length == 0)
            return;

        lock (_gate)
        {
            // Newest-wins: a fresh mirror for an address routinely binds a moment before the mirror it
            // replaces unbinds — a panel reopens, an asset is re-created on save, a gizmo/preview overlay
            // rebuilds — so the two briefly share the address. Inbound routing already targets whichever
            // object is registered now (the newest), and the superseded one detaches itself when its owner
            // disposes in the same churn. It's expected, self-resolving overlap, so we simply take over the
            // entry without logging — the old warning fired on every preview/save/layout change and buried
            // the console.
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

    private void RouteEvent(string address, string key, JToken args)
    {
        EngineObject? bound;
        lock (_gate)
            _bound.TryGetValue(address, out bound);

        // A raise for an unbound address is normal — the object may have unbound while the raise was
        // already in flight.
        bound?.RaiseFromEngine(key, args);
    }
}
