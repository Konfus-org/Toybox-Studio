using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The wire codec for <see cref="VertexBuffer"/>: <c>{ vertices: [floats], layout }</c>.</summary>
public sealed class VertexBufferConverter : IWireConverter<VertexBuffer>
{
    public VertexBuffer Read(JToken value) => ReadBuffer(value);

    public JToken Write(VertexBuffer value) => WriteBuffer(value);

    public static VertexBuffer ReadBuffer(JToken? token)
    {
        if (WireValue.Unwrap(token) is not JObject body)
            return new VertexBuffer([], new VertexBufferLayout([], 0u));

        var vertices = WireValue.Unwrap(WireValue.Field(body, "vertices")) is JArray array
            ? array.Select(token => WireValue.ReadSingle(token)).ToArray()
            : [];
        return new VertexBuffer(
            vertices, VertexBufferLayoutConverter.ReadLayout(WireValue.Field(body, "layout")));
    }

    public static JToken WriteBuffer(VertexBuffer buffer) => new JObject
    {
        ["vertices"] = new JArray(buffer.Vertices.Cast<object>().ToArray()),
        ["layout"] = VertexBufferLayoutConverter.WriteLayout(buffer.Layout),
    };
}
