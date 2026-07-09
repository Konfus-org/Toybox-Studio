using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>The wire codec for <see cref="RaycastHit"/>: the <c>physics.raycast</c> reply object.</summary>
public sealed class RaycastHitConverter : IWireConverter<RaycastHit>
{
    public RaycastHit Read(JToken value) =>
        value is JObject body
            ? new RaycastHit(
                body.Value<bool>("hasHit"),
                body.Value<ulong>("entityId"),
                WireValue.ReadVector3(body["position"]),
                body.Value<float>("fraction"))
            : default;

    public JToken Write(RaycastHit value) => new JObject
    {
        ["hasHit"] = value.HasHit,
        ["entityId"] = value.EntityId,
        ["position"] = WireValue.Write(value.Position),
        ["fraction"] = value.Fraction,
    };
}
