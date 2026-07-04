using Newtonsoft.Json.Linq;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// The base every engine-mirrored object gets — never named in user code: the source generator injects
/// it as the base type of any class opted in with <see cref="EngineSyncAttribute"/>. It carries the sync
/// plumbing the generated members call into: <see cref="Push{T}"/> behind property setters (notify, then
/// schedule the edit per its <see cref="SyncMode"/>), <see cref="Hydrate{T}"/> behind inbound applies
/// (write the field directly — never through the setter, so an engine change can't echo back), and the
/// command seam generated methods send through.
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
    }

    /// <summary>Disconnects the object: pending pushes are dropped and further edits stay local.</summary>
    public void Unbind()
    {
        var hub = _hub;
        _hub = null;
        hub?.Unregister(this);
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

        if (reply.Value is { } value)
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
            foreach (var (key, value) in body)
                if (value is not null)
                    Apply(key, value);
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
            if (value is not null)
                Apply(key, value);
    }

    /// <summary>One inbound engine change, routed here by the hub on the UI thread.</summary>
    internal void ApplyFromEngine(string key, JToken value) => Apply(key, value);

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
        RaiseChanged();
        if (_hub is { } hub && slot.Mode != SyncMode.Mirror)
            hub.Scheduler.Schedule(this, slot, CreateSetPayload(slot, slot.Write(value)));
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

    /// <summary>Routes one inbound wire key to its property (generated override; chains to the base so
    /// an inheritance chain applies end to end). True when the key was recognized.</summary>
    protected virtual bool Apply(string key, JToken value) => false;

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
}
