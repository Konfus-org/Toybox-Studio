using Newtonsoft.Json.Linq;

namespace Toybox.Studio.EngineApi;

/// <summary>The wire codec for a byte payload (e.g. a texture's decoded pixels): the engine serializes a
/// <c>std::vector&lt;unsigned char&gt;</c> as a JSON array of integers.</summary>
public sealed class ByteListConverter : IWireConverter<IReadOnlyList<byte>>
{
    public IReadOnlyList<byte> Read(JToken value) =>
        WireValue.Unwrap(value) is JArray array
            ? [.. array.Select(token => (byte)WireValue.ReadInt(token))]
            : [];

    public JToken Write(IReadOnlyList<byte> value) =>
        new JArray(value.Select(pixel => (int)pixel).Cast<object>().ToArray());
}
