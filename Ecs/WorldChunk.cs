using Toybox.Studio.Assets;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// A <c>.chunk</c> asset, mirrored from the engine's <c>WorldChunk</c>: one streamable cell of spatial
/// entities at a logical coordinate (an id in chunk space, not a world position). Lives beside the
/// entity domain because it contains <see cref="Entity"/> mirrors; the entries hydrate detached and
/// bind to the engine individually, by address.
/// </summary>
public sealed partial class WorldChunk : Asset
{
    public WorldChunk() => Entities = [];

    [EngineSync(Converter = typeof(IVec3Converter))]
    public partial IVec3 Coord { get; set; }

    /// <summary>The chunk's entities; one value — assign a new list to edit membership.</summary>
    [EngineSync(Converter = typeof(EntityListConverter))]
    public partial IReadOnlyList<Entity> Entities { get; set; }
}
