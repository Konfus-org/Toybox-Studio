using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types;

/// <summary>The wire codec for <see cref="IVec3"/>: a bare three-element integer array, matching the
/// vector shapes in <see cref="WireValue"/>.</summary>
public sealed class IVec3Converter : IWireConverter<IVec3>
{
    public IVec3 Read(JToken value) =>
        value is JArray { Count: >= 3 } array
            ? new IVec3(array[0].Value<int>(), array[1].Value<int>(), array[2].Value<int>())
            : default;

    public JToken Write(IVec3 value) => new JArray(value.X, value.Y, value.Z);
}
