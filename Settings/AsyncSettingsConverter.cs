using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Settings;

/// <summary>The wire codec for <see cref="AsyncSettings"/>: a <c>{workerCount}</c> object.</summary>
public sealed class AsyncSettingsConverter : IWireConverter<AsyncSettings>
{
    public AsyncSettings Read(JToken value) =>
        value is JObject body
            ? new AsyncSettings { WorkerCount = WireValue.ReadInt(body["workerCount"]) }
            : new AsyncSettings();

    public JToken Write(AsyncSettings value) => new JObject
    {
        ["workerCount"] = value.WorkerCount,
    };
}
