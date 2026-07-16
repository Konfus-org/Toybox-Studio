using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The wire codec for <see cref="MaterialConfig"/>: an object of camelCase render-state keys,
/// enums as their snake_case engine spellings.</summary>
public sealed class MaterialConfigConverter : IWireConverter<MaterialConfig>
{
    public MaterialConfig Read(JToken value)
    {
        if (value is not JObject body)
            return new MaterialConfig();

        return new MaterialConfig
        {
            IsDepthTestEnabled = WireValue.ReadBool(body["isDepthTestEnabled"], true),
            IsDepthWriteEnabled = WireValue.ReadBool(body["isDepthWriteEnabled"], true),
            IsDepthPrepassEnabled = WireValue.ReadBool(body["isDepthPrepassEnabled"]),
            IsTwoSided = WireValue.ReadBool(body["isTwoSided"]),
            IsCullable = WireValue.ReadBool(body["isCullable"], true),
            DepthFunction = WireValue.ReadEnum(body["depthFunction"], MaterialDepthFunction.Less),
            BlendMode = WireValue.ReadEnum(body["blendMode"], MaterialBlendMode.Opaque),
            ShadowMode = WireValue.ReadEnum(body["shadowMode"], ShadowMode.On),
        };
    }

    public JToken Write(MaterialConfig value) => new JObject
    {
        ["isDepthTestEnabled"] = value.IsDepthTestEnabled,
        ["isDepthWriteEnabled"] = value.IsDepthWriteEnabled,
        ["isDepthPrepassEnabled"] = value.IsDepthPrepassEnabled,
        ["isTwoSided"] = value.IsTwoSided,
        ["isCullable"] = value.IsCullable,
        ["depthFunction"] = WireValue.WriteEnum(value.DepthFunction),
        ["blendMode"] = WireValue.WriteEnum(value.BlendMode),
        ["shadowMode"] = WireValue.WriteEnum(value.ShadowMode),
    };
}
