using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>Mesh LOD selection for a renderable entity, mirroring the engine's <c>Lods</c>: the
/// distance bands plus the overall render distance.</summary>
public sealed partial class Lods : Component
{
    public Lods() => Values = [];

    /// <summary>The distance bands; one value — assign a new list to edit.</summary>
    [EngineSync(Converter = typeof(LodListConverter))]
    public partial IReadOnlyList<Lod> Values { get; set; }

    /// <summary>The distance beyond which the entity stops rendering entirely (zero = unlimited).</summary>
    [EngineSync]
    public partial float RenderDistance { get; set; }
}
