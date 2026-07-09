using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Settings;

/// <summary>The wire codec for <see cref="AsyncSettings"/>: a <c>{workerCount}</c> object. The field
/// reads through <see cref="WireValue.Field"/>, so the engine's serialized dialect (snake_case keys,
/// typed field envelopes) hydrates too.</summary>
public sealed class AsyncSettingsConverter : IWireConverter<AsyncSettings>
{
    public AsyncSettings Read(JToken value) =>
        WireValue.Unwrap(value) is JObject body
            ? new AsyncSettings { WorkerCount = WireValue.ReadInt(WireValue.Field(body, "workerCount")) }
            : new AsyncSettings();

    public JToken Write(AsyncSettings value) => new JObject
    {
        ["workerCount"] = value.WorkerCount,
    };
}
