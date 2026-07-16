using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Components;

/// <summary>The wire codec for a <see cref="PostProcessing"/> effect stack: an ordered array of
/// <c>{material, isEnabled, blend}</c> entries, the material as its by-value instance body.</summary>
public sealed class PostProcessingEffectListConverter : IWireConverter<IReadOnlyList<PostProcessingEffect>>
{
    private static readonly MaterialInstanceConverter MaterialConverter = new();

    public IReadOnlyList<PostProcessingEffect> Read(JToken value) =>
        value is JArray array ? [.. array.Select(ReadEffect)] : [];

    public JToken Write(IReadOnlyList<PostProcessingEffect> value) =>
        new JArray(value.Select(WriteEffect));

    private static PostProcessingEffect ReadEffect(JToken token) =>
        token is JObject body
            ? new PostProcessingEffect
            {
                Material = MaterialConverter.Read(body["material"] ?? new JObject()),
                IsEnabled = WireValue.ReadBool(body["isEnabled"], true),
                Blend = WireValue.ReadSingle(body["blend"], 1.0f),
            }
            : new PostProcessingEffect();

    private static JToken WriteEffect(PostProcessingEffect effect) => new JObject
    {
        ["material"] = MaterialConverter.Write(effect.Material),
        ["isEnabled"] = effect.IsEnabled,
        ["blend"] = effect.Blend,
    };
}
