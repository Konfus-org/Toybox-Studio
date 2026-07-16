using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The wire codec for <see cref="ShaderProgram"/>: one handle per graphics stage plus the
/// compute-stage handle array.</summary>
public sealed class ShaderProgramConverter : IWireConverter<ShaderProgram>
{
    public ShaderProgram Read(JToken value)
    {
        if (value is not JObject body)
            return new ShaderProgram();

        return new ShaderProgram
        {
            Vertex = WireValue.ReadHandle(body["vertex"]),
            Fragment = WireValue.ReadHandle(body["fragment"]),
            Tesselation = WireValue.ReadHandle(body["tesselation"]),
            Geometry = WireValue.ReadHandle(body["geometry"]),
            Computes = body["computes"] is JArray computes
                ? [.. computes.Select(WireValue.ReadHandle)]
                : [],
        };
    }

    public JToken Write(ShaderProgram value) => new JObject
    {
        ["vertex"] = WireValue.Write(value.Vertex),
        ["fragment"] = WireValue.Write(value.Fragment),
        ["tesselation"] = WireValue.Write(value.Tesselation),
        ["geometry"] = WireValue.Write(value.Geometry),
        ["computes"] = new JArray(value.Computes.Select(handle => WireValue.Write(handle))),
    };
}
