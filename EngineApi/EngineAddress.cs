namespace Toybox.Studio.EngineApi;

/// <summary>
/// The wire identity of one engine-mirrored object: a path such as <c>entity/42/Transform</c> that
/// rides in every outbound payload and routes inbound <c>sync.changed</c> deltas back to whichever
/// bound <see cref="EngineObject"/> registered under it. Normally declared as a template on the
/// class-level [EngineSync] (<c>Address = "entity/{EntityId}/{Name}"</c>) and generated from there;
/// <see cref="None"/> is right for engine-global state, which needs no address.
/// </summary>
public readonly record struct EngineAddress(string Path)
{
    /// <summary>The empty address of engine-global state.</summary>
    public static EngineAddress None => default;

    public bool IsNone => string.IsNullOrEmpty(Path);

    public override string ToString() => Path ?? string.Empty;
}
