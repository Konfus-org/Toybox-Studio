using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types.Components;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Worlds;

/// <summary>
/// The wire codec for an entity registry carried by a world container (<see cref="WorldGlobals"/>,
/// <see cref="WorldChunk"/>): an array of entity bodies. Unlike a copied entity body, a registry entry
/// IS identity, so each entry carries the entity's <c>id</c> alongside its serialized values; reading
/// hydrates detached <see cref="Entity"/> mirrors (they bind to the engine individually, by address).
/// </summary>
public sealed class EntityListConverter : IWireConverter<IReadOnlyList<Entity>>
{
    public IReadOnlyList<Entity> Read(JToken value) =>
        value is JArray array ? [.. array.OfType<JObject>().Select(ReadEntity)] : [];

    public JToken Write(IReadOnlyList<Entity> value) => new JArray(value.Select(WriteEntity));

    private static Entity ReadEntity(JObject body)
    {
        var entity = new Entity();
        entity.Deserialize(body);
        return entity;
    }

    private static JToken WriteEntity(Entity entity)
    {
        var body = entity.Serialize();
        body["id"] = WireValue.Write(entity.Id);
        return body;
    }
}
