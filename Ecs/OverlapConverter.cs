using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>The wire codec for <see cref="Overlap"/>: an object carrying the trigger's and the
/// overlapped body's entity ids.</summary>
public sealed class OverlapConverter : IWireConverter<Overlap>
{
    public Overlap Read(JToken value) =>
        value is JObject body
            ? new Overlap(body.Value<ulong>("trigger"), body.Value<ulong>("other"))
            : default;

    public JToken Write(Overlap value) => new JObject
    {
        ["trigger"] = value.TriggerEntityId,
        ["other"] = value.OtherEntityId,
    };
}
