using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The wire codec for <see cref="VertexBufferLayout"/>: <c>{ elements: [...], stride }</c>.</summary>
public sealed class VertexBufferLayoutConverter : IWireConverter<VertexBufferLayout>
{
    public VertexBufferLayout Read(JToken value) => ReadLayout(value);

    public JToken Write(VertexBufferLayout value) => WriteLayout(value);

    public static VertexBufferLayout ReadLayout(JToken? token)
    {
        if (WireValue.Unwrap(token) is not JObject body)
            return new VertexBufferLayout([], 0u);

        var elements = WireValue.Unwrap(WireValue.Field(body, "elements")) is JArray array
            ? array.Select(VertexBufferAttributeConverter.ReadAttribute).ToArray()
            : [];
        return new VertexBufferLayout(elements, (uint)WireValue.ReadUInt64(WireValue.Field(body, "stride")));
    }

    public static JToken WriteLayout(VertexBufferLayout layout) => new JObject
    {
        ["elements"] = new JArray(
            layout.Elements.Select(VertexBufferAttributeConverter.WriteAttribute).ToArray<object>()),
        ["stride"] = new JValue(layout.Stride),
    };
}
