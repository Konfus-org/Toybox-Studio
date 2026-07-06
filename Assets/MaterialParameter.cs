using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Assets;

/// <summary>
/// One material parameter keyed by shader binding name, mirroring the engine's
/// <c>MaterialParameter</c>. The engine value is a tagged variant (bool/int/float/double/vecN/colour/
/// matN), so <see cref="Data"/> is kept as the raw wire token and round-trips verbatim; the typed
/// accessors on <see cref="MaterialInstance"/> read and write it through the
/// <see cref="Toybox.Studio.EngineApi.WireValue"/> shapes.
/// </summary>
public sealed record MaterialParameter
{
    public string Name { get; init; } = string.Empty;

    public JToken Data { get; init; } = JValue.CreateNull();

    /// <summary>The entry's wire shape: <c>{name, data}</c>, data verbatim.</summary>
    internal JToken ToWire() => new JObject
    {
        ["name"] = Name,
        ["data"] = Data.DeepClone(),
    };

    /// <summary>Reads one wire entry; lenient — a malformed token yields an empty parameter.</summary>
    internal static MaterialParameter FromWire(JToken? token) =>
        token is JObject value
            ? new MaterialParameter
            {
                Name = WireValue.ReadString(value["name"]),
                Data = value["data"]?.DeepClone() ?? JValue.CreateNull(),
            }
            : new MaterialParameter();
}
