using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// The wire codec for a <see cref="MaterialInstance"/> carried by value inside another synced object
/// (a <c>Sky</c>'s material, a post-processing effect) rather than referenced as an asset: the
/// instance's serialized body — <c>{material, overrides}</c>, identity excluded — travels as one value.
/// The instance stays unbound; edits reach the engine when the owning property is reassigned.
/// </summary>
public sealed class MaterialInstanceConverter : IWireConverter<MaterialInstance>
{
    public MaterialInstance Read(JToken value)
    {
        var instance = new MaterialInstance();
        if (value is JObject body)
            instance.Deserialize(body);
        return instance;
    }

    public JToken Write(MaterialInstance value) => value.Serialize();
}
