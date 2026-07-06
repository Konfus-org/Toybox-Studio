using Toybox.Studio.Assets;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// A <c>.world</c> asset, mirrored from the engine's <c>World</c> description: the
/// <see cref="WorldGlobals"/> handle plus the <see cref="WorldChunk"/> handles it streams. The engine's
/// live entity registry is runtime state built from those containers, so a world asset only describes
/// what to load.
/// </summary>
public sealed partial class World : Asset
{
    public World() => Chunks = [];

    /// <summary>The full-lifetime resident entities (a <c>.globals</c> asset).</summary>
    [EngineSync]
    public partial Handle Globals { get; set; }

    /// <summary>The streamable spatial chunks (<c>.chunk</c> assets).</summary>
    [EngineSync(Converter = typeof(HandleListConverter))]
    public partial IReadOnlyList<Handle> Chunks { get; set; }
}
