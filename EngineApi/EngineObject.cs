using Newtonsoft.Json.Linq;
using Toybox.Studio.Utils.Attributes;
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
public abstract class EngineObject : IListenable, ISerializable, IDisposable
{
    private SyncHub? _hub;

    // The event slots that currently have handlers, kept so Bind can replay the subscriptions — the
    // engine only streams raises someone is listening to (see AddHandler/RemoveHandler).
    private List<SyncEventSlot>? _subscribedEvents;

    // The nested EngineObjects this one owns through its child-bearing synced properties (an entity's
    // components, a world's entities): bound and unbound with their owner, and their edits/changes bubble
    // up so the owner (an Asset) sees its whole graph go dirty. Kept in sync with the property values by
    // ReconcileChildren.
    private readonly List<EngineObject> _children = [];

    /// <summary>Raised after any synced value changes, from either side. Raised on the setter's thread
    /// for local edits and on the UI thread for engine-applied ones.</summary>
    public event Action? Changed;

    /// <summary>Raised for a real, local synced edit (a bound, non-Mirror value changed through the
    /// setter) — never for an inbound engine apply, an unbound assignment, or a Mirror value. Fires after
    /// the field is written and just before <see cref="Changed"/>, carrying the edited property's wire key
    /// so a listener can coalesce a run of edits to one property. The undo history listens here.</summary>
    public event Action<EditInfo>? Edited;

    /// <summary>Whether the object is bound to the engine (edits push, inbound changes apply). Engine
    /// plumbing, not content — kept out of reflective UIs like the property grid.</summary>
    [Hidden]
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
    /// Whether this object addresses its properties by path rather than by a separate key — an entity or a
    /// component, whose edits go through the engine's world-qualified <c>sync.set</c> path
    /// (<c>world/{w}/entities/{id}/components/{comp}/{key}</c>). When true, a push / reset / isDefault folds
    /// the property's wire key into the address (<c>{Address}/{key}</c>) and sends no separate <c>key</c>;
    /// when false (the default), the family carries <c>{address, key, value}</c>. The generator overrides
    /// this on the path-addressed roots.
    /// </summary>
    protected virtual bool PathAddressed => false;

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

        // Bind the children under the same hub (each at its own address); ReconcileChildren already made
        // the set current when the child-bearing properties last changed.
        foreach (var child in _children)
        {
            PrepareChild(child);
            child.Bind(hub);
        }
    }

    /// <summary>Disconnects the object: pending pushes are dropped, further edits stay local, and the
    /// engine stops streaming this object's event raises.</summary>
    public void Unbind()
    {
        var hub = _hub;
        _hub = null;
        if (hub is null)
            return;

        // Detach the children from the engine too (they stay owned — their edits still bubble — but stop
        // pushing), then unregister self.
        foreach (var child in _children)
            child.Unbind();

        if (_subscribedEvents is { } subscribed)
            foreach (var slot in subscribed)
                SendSubscription(hub, EngineCommands.SyncUnsubscribe, slot);
        hub.Unregister(this);
    }

    /// <summary>Tears the object down: unbinds it (and its children) from the engine. An owner disposes to
    /// release a whole mirror graph — a world dropping its entities and their components — in one call.
    /// Override to add teardown (dropping event-bus registrations) and chain to the base.</summary>
    public virtual void Dispose() => Unbind();

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

        var payload = CreatePropertyPayload(key);
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

        var payload = CreatePropertyPayload(key);
        return SendCommandAsync<bool>(EngineCommands.SyncIsDefault, payload, ct);
    }

    /// <summary>Re-reads the whole object from the engine (by its address) and applies every returned
    /// value — the uniform read verb, <c>sync.describe</c>.</summary>
    public async Task<Result> RefreshAsync(CancellationToken ct = default)
    {
        var reply = await SendCommandAsync<JObject>(EngineCommands.SyncDescribe, CreatePayload(), ct)
            .ContinueOnAnyContext();
        if (!reply)
            return Result.Fail(reply.Error!);

        if (reply.Value is { } body)
        {
            // The engine's describe returns the body's fields at the top level, each wrapped in its typed
            // envelope; unwrap to the bare value and apply (unknown extras like "typeName" are skipped).
            foreach (var (key, value) in body)
                if (WireValue.Unwrap(value) is { } bare)
                    Apply(key, bare);
        }

        return Result.Ok();
    }

    /// <summary>
    /// The object's synced state as a wire-shaped body — one entry per synced property, blanket (identity
    /// and engine-owned values included): the studio side serializes everything, and the engine ignores
    /// what it doesn't want (its own <c>do_not_serialize</c> governs what C++ persists). It is also what an
    /// undo snapshot of the object is built from, so it must carry the whole state.
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

    /// <summary>
    /// <see cref="Deserialize"/>'s push-through twin: applies a whole synced body and PUSHES each changed
    /// value to the engine, exactly as if the user re-entered them — <see cref="OnEdited"/> fires (an
    /// asset's dirty flag flips), <see cref="Changed"/> raises, and the edit schedules per its
    /// <see cref="SyncMode"/>. This restores an undo snapshot, where <see cref="Deserialize"/> pastes a
    /// clipboard body. Unchanged values are skipped by the setter's own equality test, so only real deltas
    /// reach the wire.
    /// </summary>
    public virtual void RestoreFrom(JObject body)
    {
        foreach (var (key, value) in body)
            if (WireValue.Unwrap(value) is { } bare)
                PushApply(key, bare);
    }

    /// <summary>
    /// The diff twin of <see cref="RestoreFrom"/>, driving an undo/redo step: pushes each property whose
    /// value in <paramref name="target"/> differs from <paramref name="from"/> — the properties the step
    /// actually changes — and leaves every other property alone. This is what undo/redo need: a blanket
    /// <see cref="RestoreFrom"/> would also push values that merely drifted in the mirror since the snapshot
    /// (a world's engine-owned globals the describe never carried, another entity's engine-driven motion),
    /// driving stale state back into the live engine; the diff pushes only the edit itself.
    /// </summary>
    public virtual void RestoreDiff(JObject target, JObject from)
    {
        foreach (var (key, value) in target)
            if (!JToken.DeepEquals(value, from[key]) && WireValue.Unwrap(value) is { } bare)
                PushApply(key, bare);
    }

    /// <summary>One inbound engine change, routed here by the hub on the UI thread.</summary>
    internal void ApplyFromEngine(string key, JToken value) =>
        Apply(key, WireValue.Unwrap(value) ?? value);

    /// <summary>One inbound engine event raise, routed here by the hub on the UI thread.</summary>
    internal void RaiseFromEngine(string key, JToken args) => Raise(key, args);

    /// <summary>
    /// The generated setter body: updates the field, notifies, and — when bound — schedules the push per
    /// the slot's mode. False (and no side effects) when the value is unchanged. Unbound or
    /// <see cref="SyncMode.OneWayFromEngine"/> edits stay local.
    /// </summary>
    protected bool Push<T>(ref T field, T value, SyncSlot slot)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        if (slot.BindsChildren)
            ReconcileChildren();
        var hub = slot.Mode != SyncMode.OneWayFromEngine ? _hub : null;
        if (hub is not null)
            OnEdited(new EditInfo(slot.Key));
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
        if (slot.BindsChildren)
            ReconcileChildren();
        RaiseChanged();
    }

    /// <summary>
    /// A real edit reached this object — either one of its own bound, non-Mirror values changed through a
    /// setter, or a nested child's edit bubbled up. As opposed to an inbound apply, an unbound assignment
    /// (constructor defaults, template authoring), or an engine-owned Mirror value. Called before the
    /// change notification, so derived edit-state (an asset's dirty flag) travels with it. The default
    /// raises <see cref="Edited"/> (which re-bubbles to this object's own owner); override to add
    /// bookkeeping and chain to the base to keep the signal.
    /// </summary>
    protected virtual void OnEdited(EditInfo edit) => Edited?.Invoke(edit);

    /// <summary>Collects the nested <see cref="EngineObject"/>s reachable through this object's
    /// child-bearing synced properties (generated override; chains to the base so an inheritance chain
    /// contributes end to end). The owner binds them and aggregates their edits.</summary>
    protected virtual void CollectChildren(List<EngineObject> children)
    {
    }

    /// <summary>Readies a child for binding — the owner stamps whatever the child's <see cref="Address"/>
    /// needs from it (an entity writes its id onto each component, whose address is
    /// <c>entity/{EntityId}/{Name}</c>). Called just before the child binds, so the address resolves.
    /// The default does nothing.</summary>
    protected virtual void PrepareChild(EngineObject child)
    {
    }

    // Brings the owned-child set in line with the current child-bearing property values: newly-present
    // children start bubbling their edits/changes here (and bind if this object is bound), removed ones
    // detach. Called when a child-bearing property changes (Push/Hydrate) and after Deserialize.
    private void ReconcileChildren()
    {
        var current = new List<EngineObject>();
        CollectChildren(current);

        foreach (var child in _children)
            if (!current.Contains(child))
            {
                child.Edited -= OnChildEdited;
                child.Changed -= RaiseChanged;
                child.Unbind();
            }

        foreach (var child in current)
            if (!_children.Contains(child))
            {
                child.Edited += OnChildEdited;
                child.Changed += RaiseChanged;
                if (_hub is { } hub)
                {
                    PrepareChild(child);
                    child.Bind(hub);
                }
            }

        _children.Clear();
        _children.AddRange(current);
    }

    // A child's edit is this object's edit too: run it through OnEdited so an owning asset flips dirty and
    // the signal bubbles one more level up (to this object's own owner).
    private void OnChildEdited(EditInfo edit) => OnEdited(edit);

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

    /// <summary>Routes one body entry to its property THROUGH the setter/push path — the push twin of
    /// <see cref="Apply"/> that <see cref="RestoreFrom"/> drives (generated override; chains to the base
    /// so an inheritance chain restores end to end). True when the key was recognized.</summary>
    protected virtual bool PushApply(string key, JToken value) => false;

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
        // A path-addressed object (entity/component) carries the property in the address itself and no
        // extras; every other family sends { address, key, value } (+ the attribute's extras).
        if (PathAddressed)
            return new JObject { ["address"] = KeyedAddress(slot.Key), ["value"] = value };

        var payload = CreatePayload();
        WriteExtras(slot.Key, payload);
        payload["key"] = slot.Key;
        payload["value"] = value;
        return payload;
    }

    // A property's full wire address: the object's path with the property's wire key appended, the shape the
    // engine's sync.set/reset/isDefault path verbs expect (world/{w}/entities/{id}/components/{comp}/{key}).
    private string KeyedAddress(string key) => $"{Address}/{key}";

    // A verb payload addressing one property: folded into the address for a path-addressed object, or the
    // object address plus a separate { key } for the keyed families.
    private JObject CreatePropertyPayload(string key)
    {
        if (PathAddressed)
            return new JObject { ["address"] = KeyedAddress(key) };

        var payload = CreatePayload();
        payload["key"] = key;
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
