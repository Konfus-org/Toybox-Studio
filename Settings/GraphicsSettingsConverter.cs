using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Settings;

/// <summary>The wire codec for <see cref="GraphicsSettings"/>: an object of camelCase keys, the
/// resolution a <c>{width, height}</c> object, enums as their snake_case engine spellings.</summary>
public sealed class GraphicsSettingsConverter : IWireConverter<GraphicsSettings>
{
    public GraphicsSettings Read(JToken value)
    {
        if (value is not JObject body)
            return new GraphicsSettings();

        return new GraphicsSettings
        {
            VsyncEnabled = WireValue.ReadEnum(body["vsyncEnabled"], VsyncMode.Off),
            GraphicsApi = WireValue.ReadEnum(body["graphicsApi"], GraphicsApi.Opengl),
            Resolution = SizeConverter.ReadSize(body["resolution"]),
            ShadowMapResolution = WireValue.ReadInt(body["shadowMapResolution"], 4096),
            ShadowRenderDistance = WireValue.ReadSingle(body["shadowRenderDistance"], 500f),
            ShadowSoftness = WireValue.ReadSingle(body["shadowSoftness"], 1f),
            LocalLightMaxDistance = WireValue.ReadSingle(body["localLightMaxDistance"], 200f),
            MinScreenSize = WireValue.ReadSingle(body["minScreenSize"], 3f),
            ScreenSizeFadeFraction = WireValue.ReadSingle(body["screenSizeFadeFraction"], 0.5f),
        };
    }

    public JToken Write(GraphicsSettings value) => new JObject
    {
        ["vsyncEnabled"] = WireValue.WriteEnum(value.VsyncEnabled),
        ["graphicsApi"] = WireValue.WriteEnum(value.GraphicsApi),
        ["resolution"] = SizeConverter.WriteSize(value.Resolution),
        ["shadowMapResolution"] = value.ShadowMapResolution,
        ["shadowRenderDistance"] = value.ShadowRenderDistance,
        ["shadowSoftness"] = value.ShadowSoftness,
        ["localLightMaxDistance"] = value.LocalLightMaxDistance,
        ["minScreenSize"] = value.MinScreenSize,
        ["screenSizeFadeFraction"] = value.ScreenSizeFadeFraction,
    };
}
