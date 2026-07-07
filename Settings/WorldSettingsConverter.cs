using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Settings;

/// <summary>The wire codec for <see cref="WorldSettings"/>: an object of camelCase keys, the startup
/// world the single-handle wire shape.</summary>
public sealed class WorldSettingsConverter : IWireConverter<WorldSettings>
{
    public WorldSettings Read(JToken value)
    {
        if (value is not JObject body)
            return new WorldSettings();

        return new WorldSettings
        {
            StartupWorld = WireValue.ReadHandle(body["startupWorld"]),
            ChunkSize = WireValue.ReadSingle(body["chunkSize"], 32f),
            KeepLoadedRadius = WireValue.ReadSingle(body["keepLoadedRadius"], 64f),
        };
    }

    public JToken Write(WorldSettings value) => new JObject
    {
        ["startupWorld"] = WireValue.Write(value.StartupWorld),
        ["chunkSize"] = value.ChunkSize,
        ["keepLoadedRadius"] = value.KeepLoadedRadius,
    };
}
