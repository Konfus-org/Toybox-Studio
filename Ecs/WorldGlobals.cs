using Toybox.Studio.Assets;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// A <c>.globals</c> asset, mirrored from the engine's <c>WorldGlobals</c>: the registry of entities
/// that stay resident for the full application lifetime. Lives beside the entity domain (like the
/// engine's own world headers) because it contains <see cref="Entity"/> mirrors; the entries hydrate
/// detached and bind to the engine individually, by address.
/// </summary>
public sealed partial class WorldGlobals : Asset
{
    public WorldGlobals() => Entities = [];

    /// <summary>The resident entities; one value — assign a new list to edit membership.</summary>
    [EngineSync(Converter = typeof(EntityListConverter))]
    public partial IReadOnlyList<Entity> Entities { get; set; }
}
