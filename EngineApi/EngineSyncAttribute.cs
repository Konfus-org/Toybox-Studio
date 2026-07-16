namespace Toybox.Studio.EngineApi;

/// <summary>
/// Marks a partial property or partial method as engine-synced; the source generator (in the sibling
/// <c>Generators</c> project) writes the implementation. On a <b>property</b> it generates the backing
/// field, the setter's push to the engine (per <see cref="SyncMode"/>), and the inbound apply that keeps
/// the value mirroring the engine; a <see cref="SyncMode.OneWayFromEngine"/> property is declared get-only (or
/// with a private setter). On a <b>method</b> it generates the body as an engine command whose payload
/// is the object's address plus the method's parameters — and with <see cref="Relay"/>, a bindable
/// <c>FooCommand</c> alongside. A method returning <c>Task&lt;Result&lt;T&gt;&gt;</c> is a query: the
/// generated body awaits the engine's reply and decodes it into <c>T</c> (via the member's
/// <see cref="Converter"/> or a built-in codec).
///
/// On an <b>event</b> (a partial event whose delegate is <c>Action&lt;T&gt;</c>) it generates the
/// accessors and the inbound raise: the engine's <c>sync.event</c> notifications route to the event by
/// the object's address and the member's <see cref="Key"/>, decoding the raise's args into <c>T</c>.
/// Subscription is demand-driven — the first handler added sends <c>sync.subscribe</c> (replayed on
/// every bind), the last removed sends <c>sync.unsubscribe</c> — so the engine only streams raises
/// someone is listening to.
///
/// On a <b>class</b> it sets the defaults that bare <c>[EngineSync]</c> members inherit, declares the
/// object's wire identity (<see cref="Address"/>), and opts the class into the sync base: the generator
/// injects <see cref="EngineObject"/> as the base type, so user code never names it. A class whose base
/// is another synced type is already rooted; a class with an unrelated base (a view model) gets no base
/// — its generated methods call through an accessible <see cref="Engine"/> member instead.
///
/// A <see cref="Converter"/> only ever maps the property's value to and from its wire shape — it is
/// parameterless and stateless. Anything else a payload needs comes from the address or from
/// <c>engineParams</c>: extra wire entries added to every push/call, in three forms — a
/// <c>"key", value</c> pair for a typed constant (<c>"isPlaying", true</c>), a lone
/// <c>nameof(Member)</c> for a sibling member whose current value rides along, and <c>"key=text"</c>
/// for a string constant (a bare string pair would be ambiguous with member references).
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Method | AttributeTargets.Event)]
public sealed class EngineSyncAttribute : Attribute
{
    /// <summary>A bare member marker: everything comes from the class-level defaults (and the
    /// named properties set here).</summary>
    public EngineSyncAttribute() => EngineParams = [];

    /// <summary>Syncs via <paramref name="syncCommand"/>, with extra wire payload entries.</summary>
    public EngineSyncAttribute(string syncCommand, params object[] engineParams)
    {
        SyncCommand = syncCommand;
        EngineParams = engineParams;
    }

    /// <summary>Syncs via <paramref name="syncCommand"/> with an explicit mode, an optional pure
    /// converter (a type implementing <see cref="IWireConverter{T}"/>), the
    /// <see cref="SyncMode.Batched"/> frequency in milliseconds (zero = the scheduler's floor), and
    /// extra wire payload entries.</summary>
    public EngineSyncAttribute(
        string syncCommand,
        SyncMode syncMode,
        Type? converter = null,
        int batchFrequencyMs = 0,
        params object[] engineParams)
    {
        SyncCommand = syncCommand;
        Mode = syncMode;
        Converter = converter;
        BatchFrequencyMs = batchFrequencyMs;
        EngineParams = engineParams;
    }

    /// <summary>The RPC method the member syncs through (an <see cref="EngineCommands"/> constant).</summary>
    public string? SyncCommand { get; }

    /// <summary>The extra wire payload entries: <c>"key", value</c> pairs, <c>nameof(Member)</c>
    /// references, and <c>"key=text"</c> string constants.</summary>
    public object[] EngineParams { get; }

    /// <summary>How often <see cref="SyncMode.Batched"/> edits go out, in milliseconds (floored at the
    /// scheduler's minimum). Zero means the floor.</summary>
    public int BatchFrequencyMs { get; }

    /// <summary>When the member's edits reach the engine; also settable by name for bare markers.</summary>
    public SyncMode Mode { get; set; }

    /// <summary>A type implementing <see cref="IWireConverter{T}"/> that replaces the built-in
    /// <see cref="WireValue"/> codec for this member; also settable by name for bare markers.</summary>
    public Type? Converter { get; set; }

    /// <summary>
    /// Class-level only: the object's <see cref="EngineAddress"/> as a template over the class's own
    /// members — e.g. <c>Address = "entity/{EntityId}/{Name}"</c>. The generator validates each
    /// placeholder and emits the <c>Address</c> override; leave unset for engine-global state (no
    /// address) or when the address needs logic a template can't express (override it by hand).
    /// </summary>
    public string? Address { get; set; }

    /// <summary>Overrides the wire key; the default is the camelCase member name.</summary>
    public string? Key { get; set; }

    /// <summary>Methods on view models only: also generate a bindable async <c>FooCommand</c> that is
    /// disabled while the call is in flight.</summary>
    public bool Relay { get; set; }
}
