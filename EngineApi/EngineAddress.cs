namespace Toybox.Studio.EngineApi;

/// <summary>
/// An engine-synced object's address on the engine side: one slash-delimited path from a root to an object (what a
/// <c>sync.describe</c> reads) or to one of its fields (what a <c>sync.set</c>/<c>reset</c>/<c>isDefault</c>
/// writes). It REPLACES the old per-tier target class hierarchy — every tier (component, entity, streamed
/// asset, settings) is now the same path, so the engine exposes ONE uniform verb set keyed by it rather than a
/// verb family per kind (<c>sync.set</c> + <c>entity.setName</c> + <c>asset.stream.set</c> + …).
///
/// Path grammar (the engine resolver walks it segment by segment — a name segment is a snake_case wire, an id
/// segment is a decimal id):
/// <code>
///   world/{worldAssetId}                                       a live world (0 = the active editing world)
///   world/{worldAssetId}/entities/{entityId}                   an entity
///   world/{worldAssetId}/entities/{entityId}/components/{wire} a component
///   …/{fieldWire}                                              a field (nested members are deeper segments)
///   stream/{destination}/{assetId}                             an asset resident in a running world (live preview)
///   settings                                                   the project's application settings
/// </code>
/// Build one through a root factory then the fluent segment methods; never hand-concatenate a path at a call site.
/// </summary>
public readonly record struct EngineAddress(string Path)
{
    /// <summary>A live world by its asset id (0 = the active editing world) — the root of its entity tree.</summary>
    public static EngineAddress World(ulong worldAssetId) => new($"world/{worldAssetId}");

    /// <summary>An asset resident (streamed) in a running world, addressed by where its live edits land.</summary>
    public static EngineAddress Stream(StreamDestination destination, ulong assetId) =>
        new($"stream/{destination.ToString().ToLowerInvariant()}/{assetId}");

    /// <summary>The open project's application settings.</summary>
    public static EngineAddress Settings { get; } = new("settings");

    /// <summary>The entity with this id, within a world root.</summary>
    public EngineAddress Entity(ulong id) => Append($"entities/{id}");

    /// <summary>The named component, on an entity root.</summary>
    public EngineAddress Component(string wire) => Append($"components/{wire}");

    /// <summary>The script with this id, on an entity root.</summary>
    public EngineAddress Script(ulong id) => Append($"scripts/{id}");

    /// <summary>A field (by wire) on this object — the leaf a set/reset/isDefault addresses.</summary>
    public EngineAddress Field(string wire) => Append(wire);

    private EngineAddress Append(string segment) => new($"{Path}/{segment}");
}
