using Newtonsoft.Json.Linq;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// Everything the sync runtime needs to know about one engine-synced event, built once per event by the
/// generated code (a static field per slot): the wire key inbound raises route by, and the codec turning
/// a raise's args into the handler's payload value.
/// </summary>
public sealed class SyncEventSlot(string key, Func<JToken, object?> read)
{
    /// <summary>The event's wire key (camelCase member name unless overridden).</summary>
    public string Key { get; } = key;

    /// <summary>Converts a raise's wire args to the handler's payload value.</summary>
    public Func<JToken, object?> Read { get; } = read;
}
