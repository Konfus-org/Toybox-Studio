using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>The wire codec for <see cref="Contact"/>: an object carrying the pair's entity ids and the
/// contact position/normal as the bare vector arrays of <see cref="WireValue"/>.</summary>
public sealed class ContactConverter : IWireConverter<Contact>
{
    public Contact Read(JToken value) =>
        value is JObject body
            ? new Contact(
                body.Value<ulong>("entity"),
                body.Value<ulong>("other"),
                WireValue.ReadVector3(body["position"]),
                WireValue.ReadVector3(body["normal"]))
            : default;

    public JToken Write(Contact value) => new JObject
    {
        ["entity"] = value.EntityId,
        ["other"] = value.OtherEntityId,
        ["position"] = WireValue.Write(value.Position),
        ["normal"] = WireValue.Write(value.Normal),
    };
}
