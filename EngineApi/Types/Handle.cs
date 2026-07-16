namespace Toybox.Studio.EngineApi.Types;

/// <summary>
/// A reference to an asset or runtime object by stable name and id, mirroring the engine's
/// <c>tbx::Handle</c>. Identity is the id; the name is a debug/authoring label the engine can also
/// resolve by. A name-only handle keeps <see cref="Id"/> zero — the engine's name→id hash
/// (<c>std::hash</c>) is not reproducible here, so the engine resolves such handles itself and the
/// assigned id flows back with the next inbound sync.
/// </summary>
public readonly record struct Handle
{
    private readonly string? _name;

    public Handle(ulong id) => Id = id;

    public Handle(string name, ulong id = 0UL)
    {
        _name = name;
        Id = id;
    }

    /// <summary>The empty reference.</summary>
    public static Handle None => default;

    /// <summary>The runtime debug label; identity (and what persists) is <see cref="Id"/>.</summary>
    public string Name => _name ?? string.Empty;

    public ulong Id { get; }

    /// <summary>Whether the handle references anything — an id, or a name the engine can resolve.</summary>
    public bool IsValid => Id != 0UL || !string.IsNullOrEmpty(_name);

    public override string ToString() => $"[Name: {Name}, Id: {Id:x}]";
}
