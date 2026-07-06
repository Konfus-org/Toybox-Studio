using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>The wire codec for <see cref="Viewport"/>: <c>{position, dimensions}</c> with the position
/// as a bare two-element array and the dimensions as a <c>{width, height}</c> object.</summary>
public sealed class ViewportConverter : IWireConverter<Viewport>
{
    public Viewport Read(JToken value) =>
        value is JObject body
            ? new Viewport
            {
                Position = WireValue.ReadVector2(body["position"]),
                Dimensions = SizeConverter.ReadSize(body["dimensions"]),
            }
            : new Viewport();

    public JToken Write(Viewport value) => new JObject
    {
        ["position"] = WireValue.Write(value.Position),
        ["dimensions"] = SizeConverter.WriteSize(value.Dimensions),
    };
}
