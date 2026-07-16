using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The wire codec for <see cref="MaterialOverrides"/>: the texture and parameter override
/// arrays under one object (the derived has-override flags never travel); the array shapes are the
/// binding-container converters'.</summary>
public sealed class MaterialOverridesConverter : IWireConverter<MaterialOverrides>
{
    public MaterialOverrides Read(JToken value)
    {
        if (value is not JObject body)
            return new MaterialOverrides();

        return new MaterialOverrides
        {
            Textures = MaterialTextureBindingsConverter.ReadBindings(body["textures"]),
            Parameters = MaterialParameterBindingsConverter.ReadParameters(body["parameters"]),
        };
    }

    public JToken Write(MaterialOverrides value) => new JObject
    {
        ["textures"] = MaterialTextureBindingsConverter.WriteBindings(value.Textures),
        ["parameters"] = MaterialParameterBindingsConverter.WriteParameters(value.Parameters),
    };
}
