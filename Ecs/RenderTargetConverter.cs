using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>The wire codec for <see cref="RenderTarget"/>: the handle's <c>{name, id}</c> flattened
/// with a <c>size</c> object, matching the engine type's Handle inheritance.</summary>
public sealed class RenderTargetConverter : IWireConverter<RenderTarget>
{
    public RenderTarget Read(JToken value) =>
        value is JObject body
            ? new RenderTarget
            {
                Handle = WireValue.ReadHandle(value),
                Size = SizeConverter.ReadSize(body["size"]),
            }
            : new RenderTarget();

    public JToken Write(RenderTarget value)
    {
        var body = (JObject)WireValue.Write(value.Handle);
        body["size"] = SizeConverter.WriteSize(value.Size);
        return body;
    }
}
