using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// One material parameter keyed by shader binding name, mirroring the engine's
/// <c>MaterialParameter</c>. The engine value is a tagged variant (bool/int/float/vecN/colour/…), held
/// as a typed <see cref="MaterialValue"/> so the reflection grid can edit it with a real value editor;
/// the wire shape (<c>{name, data}</c>, data a bare value) is unchanged.
/// </summary>
public sealed record MaterialParameter
{
    public string Name { get; init; } = string.Empty;

    public MaterialValue Value { get; init; } = new();

    /// <summary>The entry's wire shape: <c>{name, data}</c>, data the value's bare wire token.</summary>
    internal JToken ToWire() => new JObject
    {
        ["name"] = Name,
        ["data"] = Value.ToWire(),
    };

    /// <summary>Reads one wire entry; lenient — a malformed token yields an empty parameter.</summary>
    internal static MaterialParameter FromWire(JToken? token) =>
        token is JObject value
            ? new MaterialParameter
            {
                Name = WireValue.ReadString(value["name"]),
                Value = MaterialValue.FromWire(value["data"]),
            }
            : new MaterialParameter();
}
