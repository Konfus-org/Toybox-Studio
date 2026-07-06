using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>The wire codec for a <see cref="Lods"/> band list: an ordered array of
/// <c>{handle, maxDistance}</c> entries.</summary>
public sealed class LodListConverter : IWireConverter<IReadOnlyList<Lod>>
{
    public IReadOnlyList<Lod> Read(JToken value) =>
        value is JArray array ? [.. array.Select(ReadLod)] : [];

    public JToken Write(IReadOnlyList<Lod> value) => new JArray(value.Select(WriteLod));

    private static Lod ReadLod(JToken token) =>
        token is JObject body
            ? new Lod
            {
                Handle = WireValue.ReadHandle(body["handle"]),
                MaxDistance = WireValue.ReadSingle(body["maxDistance"]),
            }
            : new Lod();

    private static JToken WriteLod(Lod lod) => new JObject
    {
        ["handle"] = WireValue.Write(lod.Handle),
        ["maxDistance"] = lod.MaxDistance,
    };
}
