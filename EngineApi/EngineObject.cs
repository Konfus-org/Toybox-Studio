using Newtonsoft.Json.Linq;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// The base every engine-mirrored object gets — never named in user code: the source generator injects
/// it as the base type of any class opted in with <see cref="EngineSyncAttribute"/>. It carries the sync
/// plumbing the generated members call into: <see cref="Push{T}"/> behind property setters (notify, then
/// schedule the edit per its <see cref="SyncMode"/>), <see cref="Hydrate{T}"/> behind inbound applies
/// (write the field directly — never through the setter, so an engine change can't echo back), the
/// command/query seams generated methods send through, and the accessor/raise plumbing behind partial
/// events (engine → studio callbacks, streamed only while subscribed).
///
/// An object is inert until <see cref="Bind"/> hands it the <see cref="SyncHub"/>: unbound pushes just
/// update the local value (which is what makes constructor defaults safe). <see cref="Address"/> is the
/// object's wire identity (see <see cref="EngineAddress"/>), normally generated from the class-level
/// attribute's <c>Address</c> template. Change notification is the editor's <see cref="IListenable"/>;
/// anything view-bound wraps this in an explicit view model.
/// </summary>
public abstract class EngineObject : IListenable, ISerializable
{
    private SyncHub? _hub;

    // The event slots that currently have handlers, kept so Bind can replay the subscriptions — the
    // engine only streams raises someone is listening to (see AddHandler/RemoveHandler).
    private List<SyncEventSlot>? _subscribedEvents;

    /// <summary>Raised after any synced value changes, from either side. Raised on the setter's thread
    /// for local edits and on the UI thread for engine-applied ones.</summary>
    public event Action? Changed;

    /// <summary>Whether the object is bound to the engine (edits push, inbound changes apply).</summary>
    public bool IsBound => _hub is not null;

    /// <summary>The address path the hub registers the object under (the internal accessor lets the hub
    /// read the protected <see cref="Address"/>).</summary>
    internal string AddressKey => Address.ToString();

    /// <summary>
    /// The object's wire identity, sent as <c>address</c> in every outbound payload and matched against
    /// inbound change notifications. Generated from the class-level attribute's <c>Address</c> template;
    /// override by hand only when the address needs logic a template can't express. The default
    /// (<see cref="EngineAddress.None"/>) is right for engine-global state.
    /// </summary>
    protected virtual EngineAddress Address => EngineAddress.None;

    /// <summary>
    /// Connects the object to the engine: outbound edits start pushing and inbound engine changes start
    /// applying. Bind after hydration, and re-bind if <see cref="Address"/> changes (say, once an id is
    /// assigned) — registration keys on the address's value at bind time.
    /// </summary>
    public void Bind(SyncHub hub)
    {
        Unbind();
        _hub = hub;
        hub.Register(this);
        if (_subscribedEvents is { } subscribed)
            foreach (var slot in subscribed)
                SendSubscription(hub, EngineCommands.SyncSubscribe, slot);
    }

    /// <summary>Disconnects the object: pending pushes are dropped, further edits stay local, and the
    /// engine stops streaming this object's event raises.</summary>
    public void Unbind()
    {
        var hub = _hub;
        _hub = null;
        if (hub is null)
            return;

        if (_subscribedEvents is { } subscribed)
            foreach (var slot in subscribed)
                SendSubscription(hub, EngineCommands.SyncUnsubscribe, slot);
        hub.Unregister(this);
    }

    /// <summary>Sends every staged edit — <see cref="SyncMode.Manual"/> values and any batched push
    /// still waiting on its window — as one flush.</summary>
    public Task<Result> CommitAsync(CancellationToken ct = default) =>
        _hub is { } hub
            ? hub.Scheduler.FlushAsync(this, ct)
            : Task.FromResult(Result.Fail("The object is not bound to the engine."));

    /// <summary>Asks the engine to reset a synced property to its default; the reply's value is applied
    /// locally. Pass the C# property name (<c>nameof(Position)</c>).</summary>
    public async Task<Result> ResetAsync(string propertyName, CancellationToken ct = default)
    {
        if (WireKeyFor(propertyName) is not { } key)
            return Result.Fail($"'{propertyName}' is not an engine-synced property.");

        var payload = CreatePayload();
        payload["key"] = key;
        var reply = await SendCommandAsync<JToken>(EngineCommands.SyncReset, payload, ct).ContinueOnAnyContext();
        if (!reply)
            return Result.Fail(reply.Error!);

        if (WireValue.Unwrap(reply.Value) is { } value)
            Apply(key, value);
        return Result.Ok();
    }

    /// <summary>Asks the engine whether a synced property still holds its default value. Pass the C#
    /// property name (<c>nameof(Position)</c>).</summary>
    public Task<Result<bool>> IsDefaultAsync(string propertyName, CancellationToken ct = default)
    {
        if (WireKeyFor(propertyName) is not { } key)
            return Task.FromResult(Result<bool>.Fail($"'{propertyName}' is not an engine-synced property."));

        var payload = CreatePayload();
        payload["key"] = key;
        return SendCommandAsync<bool>(EngineCommands.SyncIsDefault, payload, ct);
    }

    /// <summary>Re-reads the whole object from the engine and applies every returned value.</summary>
    public async Task<Result> RefreshAsync(CancellationToken ct = default)
    {
        var reply = await SendCommandAsync<JObject>(EngineCommands.SyncDescribe, CreatePayload(), ct)
            .ContinueOnAnyContext();
        if (!reply)
            return Result.Fail(reply.Error!);

        if (reply.Value is { } body)
        {
            // The engine's describe wraps each field in its typed envelope; the slot readers want
            // the bare value.
            foreach (var (key, value) in body)
                if (WireValue.Unwrap(value) is { } bare)
                    Apply(key, bare);
        }

        return Result.Ok();
    }

    /// <summary>
    /// The object's synced state as a wire-shaped body: one entry per synced property, excluding
    /// <see cref="SyncMode.Mirror"/> values — those are engine-owned (ids and the like), so a copied
    /// body carries content, never identity.
    /// </summary>
    public JObject Serialize()
    {
        var body = new JObject();
        CollectInto(body);
        return body;
    }

    /// <summary>
    /// Applies every recognized entry of <paramref name="body"/> to the local state (unknown keys are
    /// ignored), raising <see cref="Changed"/> per changed value. Purely local — like an inbound engine
    /// apply, nothing is pushed; whoever pastes onto a live object decides how the result reaches the
    /// engine.
    /// </summary>
    public void Deserialize(JObject body)
    {
        foreach (var (key, value) in body)
            if (WireValue.Unwrap(value) is { } bare)
                Apply(key, bare);
    }

    /// <summary>One inbound engine change, routed here by the hub on the UI thread.</summary>
    internal void ApplyFromEngine(string key, JToken value) =>
        Apply(key, WireValue.Unwrap(value) ?? value);

    /// <summary>One inbound engine event raise, routed here by the hub on the UI thread.</summary>
    internal void RaiseFromEngine(string key, JToken args) => Raise(key, args);

    /// <summary>
    /// The generated setter body: updates the field, notifies, and — when bound — schedules the push per
    /// the slot's mode. False (and no side effects) when the value is unchanged. Unbound or
    /// <see cref="SyncMode.Mirror"/> edits stay local.
    /// </summary>
    protected bool Push<T>(ref T field, T value, SyncSlot slot)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        var hub = slot.Mode != SyncMode.Mirror ? _hub : null;
        if (hub is not null)
            OnEdited();
        RaiseChanged();
        hub?.Scheduler.Schedule(this, slot, CreateSetPayload(slot, slot.Write(value)));
        return true;
    }

    /// <summary>
    /// The generated apply body for one property: converts the wire value and writes the field directly
    /// — deliberately not through the setter, so an engine-applied change can never push back out.
    /// </summary>
    protected void Hydrate<T>(ref T field, JToken value, SyncSlot slot)
    {
        var incoming = (T)slot.Read(value)!;
        if (EqualityComparer<T>.Default.Equals(field, incoming))
            return;

        field = incoming;
        RaiseChanged();
    }

    /// <summary>
    /// A bound, non-Mirror value just changed locally — a real edit heading for the engine, as opposed
    /// to an inbound apply, an unbound assignment (constructor defaults, template authoring), or an
    /// engine-owned Mirror value. Called before the change notification, so derived edit-state (an
    /// asset's dirty flag) travels with it. The default does nothing.
    /// </summary>
    protected virtual void OnEdited()
    {
    }

    /// <summary>
    /// The generated add-accessor body for a synced event: the first handler subscribes the event with
    /// the engine — immediately when bound, recorded and replayed by <see cref="Bind"/> otherwise — so
    /// the engine only streams raises someone is listening to.
    /// </summary>
    protected void AddHandler<T>(ref Action<T>? field, Action<T>? value, SyncEventSlot slot)
    {
        var wasEmpty = field is null;
        field += value;
        if (!wasEmpty || field is null)
            return;

        (_subscribedEvents ??= []).Add(slot);
        if (_hub is { } hub)
            SendSubscription(hub, EngineCommands.SyncSubscribe, slot);
    }

    /// <summary>The generated remove-accessor body: the last handler leaving unsubscribes.</summary>
    protected void RemoveHandler<T>(ref Action<T>? field, Action<T>? value, SyncEventSlot slot)
    {
        var hadHandlers = field is not null;
        field -= value;
        if (!hadHandlers || field is not null)
            return;

        _subscribedEvents?.Remove(slot);
        if (_hub is { } hub)
            SendSubscription(hub, EngineCommands.SyncUnsubscribe, slot);
    }

    /// <summary>The generated raise body for one event: decodes the args and invokes the handlers.</summary>
    protected static void RaiseEvent<T>(Action<T>? handlers, JToken args, SyncEventSlot slot)
    {
        if (handlers is null)
            return;

        handlers((T)slot.Read(args)!);
    }

    /// <summary>Routes one inbound wire key to its property (generated override; chains to the base so
    /// an inheritance chain applies end to end). True when the key was recognized.</summary>
    protected virtual bool Apply(string key, JToken value) => false;

    /// <summary>Routes one inbound event raise to its event (generated override; chains to the base so
    /// an inheritance chain raises end to end). True when the key was recognized.</summary>
    protected virtual bool Raise(string key, JToken args) => false;

    /// <summary>Collects the synced, non-Mirror property values into <paramref name="body"/> (generated
    /// override; chains to the base so an inheritance chain serializes end to end).</summary>
    protected virtual void CollectInto(JObject body)
    {
    }

    /// <summary>Maps a C# property name to its wire key (generated override); null when the property
    /// is not engine-synced.</summary>
    protected virtual string? WireKeyFor(string propertyName) => null;

    /// <summary>Adds a property's extra payload entries — the attribute's <c>args</c> tail — to an
    /// outbound push (generated override).</summary>
    protected virtual void WriteExtras(string key, JObject payload)
    {
    }

    /// <summary>Sends an engine command (generated method bodies call this); a failure when unbound.</summary>
    protected Task<Result> SendCommandAsync(string command, JObject payload, CancellationToken ct) =>
        _hub is { } hub
            ? hub.Engine.SendCommandAsync(command, payload, ct)
            : Task.FromResult(Result.Fail("The object is not bound to the engine."));

    /// <summary>Sends an engine query and decodes its reply (generated typed-reply method bodies call
    /// this); a failure when unbound or when the engine sends no reply value.</summary>
    protected async Task<Result<T>> SendQueryAsync<T>(
        string command, JObject payload, Func<JToken, T> read, CancellationToken ct)
    {
        if (_hub is not { } hub)
            return Result<T>.Fail("The object is not bound to the engine.");

        var reply = await hub.Engine.SendCommandAsync<JToken>(command, payload, ct).ContinueOnAnyContext();
        return reply is { Success: true, Value: { } value }
            ? Result<T>.Ok(read(value))
            : Result<T>.Fail(reply.Error ?? "The engine sent no reply.");
    }

    /// <summary>A fresh outbound payload carrying the object's <see cref="Address"/> (when it has one).</summary>
    protected JObject CreatePayload()
    {
        var payload = new JObject();
        if (Address is { IsNone: false } address)
            payload["address"] = address.ToString();
        return payload;
    }

    /// <summary>Notifies listeners of a change; generated code raises through <see cref="Push{T}"/> and
    /// <see cref="Hydrate{T}"/>, subclasses may raise for their own derived state.</summary>
    protected void RaiseChanged() => Changed?.Invoke();

    private Task<Result<T>> SendCommandAsync<T>(string command, JObject payload, CancellationToken ct) =>
        _hub is { } hub
            ? hub.Engine.SendCommandAsync<T>(command, payload, ct)
            : Task.FromResult(Result<T>.Fail("The object is not bound to the engine."));

    private JObject CreateSetPayload(SyncSlot slot, JToken value)
    {
        var payload = CreatePayload();
        WriteExtras(slot.Key, payload);
        payload["key"] = slot.Key;
        payload["value"] = value;
        return payload;
    }

    // Subscription changes are fire-and-forget notifications: there is nothing to await in an event
    // accessor, and a lost subscribe self-heals on the next Bind (the engine drops its table per
    // connection anyway).
    private void SendSubscription(SyncHub hub, string command, SyncEventSlot slot)
    {
        var payload = CreatePayload();
        payload["key"] = slot.Key;
        _ = hub.Engine.SendNotificationAsync(command, payload);
    }
}
