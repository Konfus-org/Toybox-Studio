using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The wire codec for <see cref="VertexBufferAttribute"/>:
/// <c>{ debug_name, type, offset, normalized }</c>.</summary>
public sealed class VertexBufferAttributeConverter : IWireConverter<VertexBufferAttribute>
{
    public VertexBufferAttribute Read(JToken value) => ReadAttribute(value);

    public JToken Write(VertexBufferAttribute value) => WriteAttribute(value);

    public static VertexBufferAttribute ReadAttribute(JToken? token) =>
        WireValue.Unwrap(token) is JObject body
            ? new VertexBufferAttribute(
                WireValue.ReadString(WireValue.Field(body, "debug_name")),
                WireValue.ReadEnum<VertexFormat>(WireValue.Field(body, "type")),
                (uint)WireValue.ReadUInt64(WireValue.Field(body, "offset")),
                WireValue.ReadBool(WireValue.Field(body, "normalized")))
            : new VertexBufferAttribute(string.Empty, VertexFormat.Float, 0u, false);

    public static JToken WriteAttribute(VertexBufferAttribute attribute) => new JObject
    {
        ["debug_name"] = WireValue.Write(attribute.DebugName),
        ["type"] = WireValue.WriteEnum(attribute.Type),
        ["offset"] = new JValue(attribute.Offset),
        ["normalized"] = WireValue.Write(attribute.Normalized),
    };
}
