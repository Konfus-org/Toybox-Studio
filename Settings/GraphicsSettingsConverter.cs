using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Settings;

/// <summary>The wire codec for <see cref="GraphicsSettings"/>: an object of camelCase keys, the
/// resolution a <c>{width, height}</c> object, enums as their snake_case engine spellings. Fields
/// read through <see cref="WireValue.Field"/>, so the engine's serialized dialect (snake_case keys,
/// typed field envelopes) hydrates just as well as the editor's own writes.</summary>
public sealed class GraphicsSettingsConverter : IWireConverter<GraphicsSettings>
{
    public GraphicsSettings Read(JToken value)
    {
        if (WireValue.Unwrap(value) is not JObject body)
            return new GraphicsSettings();

        return new GraphicsSettings
        {
            VsyncEnabled = WireValue.ReadEnum(WireValue.Field(body, "vsyncEnabled"), VsyncMode.Off),
            GraphicsApi = WireValue.ReadEnum(WireValue.Field(body, "graphicsApi"), GraphicsApi.Opengl),
            Resolution = SizeConverter.ReadSize(WireValue.Field(body, "resolution")),
            ShadowMapResolution = WireValue.ReadInt(WireValue.Field(body, "shadowMapResolution"), 4096),
            ShadowRenderDistance = WireValue.ReadSingle(WireValue.Field(body, "shadowRenderDistance"), 500f),
            ShadowSoftness = WireValue.ReadSingle(WireValue.Field(body, "shadowSoftness"), 1f),
            LocalLightMaxDistance = WireValue.ReadSingle(WireValue.Field(body, "localLightMaxDistance"), 200f),
            MinScreenSize = WireValue.ReadSingle(WireValue.Field(body, "minScreenSize"), 3f),
            ScreenSizeFadeFraction = WireValue.ReadSingle(WireValue.Field(body, "screenSizeFadeFraction"), 0.5f),
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
