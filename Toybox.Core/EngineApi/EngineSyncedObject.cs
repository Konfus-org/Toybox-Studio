using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// The shared core of every engine-synced editor object — components and assets alike. Subclasses declare
/// <see cref="EngineSyncAttribute"/> fields; the source generator emits each field's public, change-notifying
/// property whose setter calls <see cref="Sync"/> to push the edit (and overrides <see cref="ApplyField"/> to
/// route engine-pushed values back). Being an <see cref="ObservableObject"/>, a reflected object binds straight
/// into XAML.
///
/// Edits sync only once <see cref="Bind"/> has attached a <see cref="EngineAddress"/> + <see cref="IEngineSyncScheduler"/>;
/// before that (construction, hydration) the generated setters are inert, so seeding defaults never pushes.
/// </summary>
public abstract class EngineSyncedObject : ObservableObject, ISerializable
{
    private EngineAddress? _address;
    private IEngineSyncScheduler? _scheduler;
    private bool _suppress;

    /// <summary>The raw describe JSON this object was last hydrated from (the engine's <c>{ field: { …, value } }</c>
    /// body), kept current as deltas arrive. Always available — the inspector grid parses it, copy/paste reads it,
    /// and a buffered asset save folds its reflected edits back into it. Empty until first hydrated.</summary>
    public JObject Raw { get; private set; } = new();

    /// <summary>The wire names this object's reflected fields cover, for load-time reconciliation against the
    /// engine's describe (a C# field with no engine counterpart is a typo or a rename). Overridden by the
    /// generator; empty for an object that declares no reflected fields of its own.</summary>
    public virtual IReadOnlyList<string> SyncedWires => [];

    /// <summary>The reflected fields' current values as a bare <c>{ wire: value }</c> object — the inverse of the
    /// hydrate/apply path. Overridden by the generator; empty for an object with no reflected fields. Used to fold
    /// a typed object's edits back into an asset's describe body before a buffered save.</summary>
    public virtual JObject CollectSynced() => new();

    /// <summary>Maps a public reflected property name to its engine wire name (generator-overridden); null when the
    /// name isn't a reflected field. Backs expression-based targeting (Reset / IsDefault by lambda).</summary>
    public virtual string? WireFor(string property) => null;

    /// <summary>Whether this object is attached to an engine address, so its edits sync.</summary>
    protected bool IsBound => _address is not null && _scheduler is not null;

    /// <summary>
    /// Applies engine-pushed field values to the local state WITHOUT echoing them back (the setters run under
    /// suppression, so they notify the UI but don't re-push). Marshalled to the UI thread, since it mutates
    /// bindable state. Unknown wire names are ignored — the engine may carry fields this object doesn't model.
    /// </summary>
    public void ApplyFromEngine(JObject delta) => Dispatch.To(DispatchContext.UI, () => Apply(delta));

    /// <summary>Writes one reflected field (by wire) in place — the immediate, awaitable push the grid's typed
    /// commit uses, bypassing the per-field timing. Fails when unbound. Signals a user edit (so the owning asset
    /// dirties), like the generated setters do.</summary>
    internal Task<Result> SetWireAsync(string wire, JToken value, CancellationToken ct)
    {
        OnFieldEdited(wire);
        return _address is { } address && _scheduler is { } scheduler
            ? scheduler.SetAsync(address, wire, value, ct)
            : Task.FromResult(Result.Fail("This object isn't bound to the engine."));
    }

    /// <summary>Resets one reflected field (by wire) to its engine default — only meaningful for a bound
    /// component. Backs the expression-based <c>ResetAsync</c> extension.</summary>
    internal Task<Result> ResetWireAsync(string wire, CancellationToken ct)
    {
        OnFieldEdited(wire);
        return _address is { } address && _scheduler is { } scheduler
            ? scheduler.ResetAsync(address, wire, ct)
            : Task.FromResult(Result.Fail("This object isn't bound to the engine."));
    }

    /// <summary>Whether one reflected field (by wire) currently holds its engine default — only meaningful for a
    /// bound component. Backs the expression-based <c>IsDefaultAsync</c> extension.</summary>
    internal Task<Result<bool>> IsDefaultWireAsync(string wire, CancellationToken ct) =>
        _address is { } address && _scheduler is { } scheduler
            ? scheduler.IsDefaultAsync(address, wire, ct)
            : Task.FromResult(Result<bool>.Fail("This object isn't bound to the engine."));

    /// <summary>Re-reads this object's whole serialized body from the engine (its <c>{ field: { …, value } }</c>
    /// describe form). Fails when unbound. The awaitable read behind copy/"give me the JSON" flows.</summary>
    internal Task<Result<JObject>> DescribeSelfAsync(CancellationToken ct) =>
        _address is { } address && _scheduler is { } scheduler
            ? scheduler.DescribeAsync(address, ct)
            : Task.FromResult(Result<JObject>.Fail("This object isn't bound to the engine."));

    /// <summary>Flushes any staged (Manual / pending Timer) edits to the engine; a no-op until bound.</summary>
    public Task<Result> CommitAsync(CancellationToken ct = default) =>
        _address is { } address && _scheduler is { } scheduler
            ? scheduler.FlushAsync(address, ct)
            : Task.FromResult(Result.Ok());

    /// <summary>Re-reads this object's current state from the engine and re-hydrates its fields (and
    /// <see cref="Raw"/>) — the manual counterpart to the live push, for <see cref="EngineSyncMode.Hydrate"/> /
    /// <see cref="EngineSyncMode.ReadOnly"/> read state. The describe + apply are echo-suppressed and marshalled
    /// to the UI thread. Fails when unbound or when the target can't describe itself. A container whose "refresh"
    /// means something richer (a <c>World</c> re-pulling its whole entity tree) overrides this.</summary>
    public virtual async Task<Result> RefreshAsync(CancellationToken ct = default)
    {
        if (_address is not { } address || _scheduler is not { } scheduler)
            return Result.Fail("This object isn't bound to the engine.");

        var result = await scheduler.DescribeAsync(address, ct).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } body })
            return Result.Fail(result.Error ?? "The engine returned no data.");

        Dispatch.To(DispatchContext.UI, () => HydrateFromDescribe(body));
        return Result.Ok();
    }

    /// <summary>Applies engine values synchronously at load (the caller is already on a known context and the
    /// object isn't bound yet, so there's nothing to echo or marshal). Used to seed a freshly-loaded object.</summary>
    internal void Hydrate(JObject delta) => Apply(delta);

    /// <summary>
    /// This object's whole serialized body as the engine's describe JSON — its last-hydrated <see cref="Raw"/>
    /// with the current reflected-field values folded back in, so it captures live edits too. The uniform "give
    /// me the JSON" read every reflected object shares (copy/paste, buffered save). A container whose live body
    /// isn't simply its own <see cref="Raw"/> overrides it.
    /// </summary>
    public virtual JObject Serialize()
    {
        var body = (JObject)Raw.DeepClone();
        WriteSyncedInto(body);
        return body;
    }

    /// <summary>
    /// Applies a serialized body (from <see cref="Serialize"/>) back onto this object — the uniform paste/restore
    /// every reflected object shares. The base seeds the reflected fields in place (an unbound / buffered object,
    /// persisted on its next save); a bound object that must push its edits to the engine, or one with structural
    /// children (an entity's components), overrides it.
    /// </summary>
    public virtual Task<Result> Deserialize(JObject body, CancellationToken ct = default)
    {
        HydrateFromDescribe(body);
        return Task.FromResult(Result.Ok());
    }

    /// <summary>
    /// Applies one grid-edited field's bare value to this object BY WIRE, through its generated property setter
    /// (NOT suppressed) — so a bound object (a live component / previewed asset) pushes the edit to the engine,
    /// and an unbound object (a buffered asset) just updates the field, to be persisted on Save. This is the
    /// inspector's typed commit path: the property grid drives the typed model and the reflection layer owns the
    /// RPC, so the grid never builds a <c>reflect.set</c> / describe-body payload itself. Unknown wires are
    /// ignored (the base <see cref="ApplyField"/> is a no-op).
    /// </summary>
    public void CommitWire(string wire, JToken value) => ApplyField(wire, value);

    /// <summary>Hydrates the reflected fields from a describe body (each field a <c>{ …, value }</c> node),
    /// unwrapping to the bare values the field readers expect. The generic seed path an asset/component loader
    /// uses after a describe; a no-op for an object with no reflected fields.</summary>
    internal void HydrateFromDescribe(JObject body)
    {
        // Raw is the source of truth for every reflected object (typed or not), so it's set even when the object
        // models no reflected fields of its own (an UnknownComponent the grid still renders from Raw).
        Raw = body;
        if (SyncedWires.Count == 0)
            return;

        var delta = new JObject();
        foreach (var property in body.Properties())
            delta[property.Name] = property.Value.Unwrap();
        Apply(delta);
    }

    /// <summary>Folds the reflected fields' current values into a describe body in place — overwriting each
    /// field node's <c>"value"</c> while preserving its attributes — so an asset's buffered save persists the
    /// typed edits alongside the unmodeled fields the body already carries. A no-op when there are no reflected
    /// fields.</summary>
    internal void WriteSyncedInto(JObject body)
    {
        foreach (var (wire, value) in CollectSynced())
        {
            if (body[wire] is JObject node && node.ContainsKey(EngineKeys.Value))
                node[EngineKeys.Value] = value;
            else
                body[wire] = value;
        }
    }

    /// <summary>Attaches this object to its engine address + scheduler, after which edits begin syncing. Called by
    /// the loading code once the object's fields have been hydrated from a describe.</summary>
    protected void Bind(EngineAddress address, IEngineSyncScheduler scheduler)
    {
        _address = address;
        _scheduler = scheduler;
    }

    /// <summary>Called by generated setters to schedule a field's new value to the engine per its mode. Inert
    /// while suppressed (applying an engine push); signals a user edit (even when unbound, so a buffered asset
    /// still dirties) then pushes to the engine when bound.</summary>
    protected void Sync(string wire, JToken value, EngineSyncMode mode, int windowMs)
    {
        if (_suppress)
            return;

        OnFieldEdited(wire);
        if (_address is not { } address || _scheduler is null)
            return;

        _scheduler.Push(address, wire, value, mode, windowMs);
    }

    /// <summary>Called when a reflected field is edited by the user (through a generated setter or the grid's
    /// <see cref="SetWireAsync"/>/<see cref="ResetWireAsync"/>) — but NOT during hydration/echo, which is
    /// suppressed. Fires whether or not the object is bound, so an owner (an asset marking itself dirty, or a
    /// world hearing its entities) can react without every edit site calling it by hand. No-op by default.</summary>
    protected virtual void OnFieldEdited(string wire)
    {
    }

    /// <summary>Routes one engine-pushed field, by wire name, to its setter. The generator overrides this for a
    /// type's own fields and chains to <c>base</c> for inherited ones; the base is a no-op (an object with no
    /// reflected fields of its own).</summary>
    protected virtual void ApplyField(string wire, JToken value)
    {
    }

    // The shared suppressed apply loop behind Hydrate (sync) and ApplyFromEngine (UI-marshalled): set each field
    // through its generated setter while suppressed, so the UI updates but nothing echoes back to the engine.
    private void Apply(JObject delta)
    {
        var was = _suppress;
        _suppress = true;
        try
        {
            foreach (var (wire, token) in delta)
                if (token is not null)
                {
                    ApplyField(wire, token);
                    // Keep Raw current for an engine-pushed delta so a later read sees the live value (a no-op
                    // during a full describe hydrate, which already set Raw to the whole body).
                    if (Raw[wire] is JObject node && node.ContainsKey(EngineKeys.Value))
                        node[EngineKeys.Value] = token;
                }
        }
        finally
        {
            _suppress = was;
        }
    }
}
