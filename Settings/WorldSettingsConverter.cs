using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Settings;

/// <summary>The wire codec for <see cref="WorldSettings"/>: an object of camelCase keys, the startup
/// world the single-handle wire shape. Fields read through <see cref="WireValue.Field"/>, so the
/// engine's serialized dialect (snake_case keys, typed field envelopes) hydrates too.</summary>
public sealed class WorldSettingsConverter : IWireConverter<WorldSettings>
{
    public WorldSettings Read(JToken value)
    {
        if (WireValue.Unwrap(value) is not JObject body)
            return new WorldSettings();

        return new WorldSettings
        {
            StartupWorld = WireValue.ReadHandle(WireValue.Field(body, "startupWorld")),
            ChunkSize = WireValue.ReadSingle(WireValue.Field(body, "chunkSize"), 32f),
            KeepLoadedRadius = WireValue.ReadSingle(WireValue.Field(body, "keepLoadedRadius"), 64f),
        };
    }

    public JToken Write(WorldSettings value) => new JObject
    {
        ["startupWorld"] = WireValue.Write(value.StartupWorld),
        ["chunkSize"] = value.ChunkSize,
        ["keepLoadedRadius"] = value.KeepLoadedRadius,
    };
}
