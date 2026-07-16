using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils.Attributes;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>One texture binding keyed by shader binding name, mirroring the engine's
/// <c>MaterialTextureBinding</c>.</summary>
public sealed record MaterialTextureBinding
{
    public string Name { get; init; } = string.Empty;

    [AssetType("png", "jpg", "jpeg", "bmp", "tga", "dds", "hdr", "exr", "ktx", "ktx2")]
    public Handle Texture { get; init; }

    /// <summary>The entry's wire shape: <c>{name, texture}</c>.</summary>
    internal JToken ToWire() => new JObject
    {
        ["name"] = Name,
        ["texture"] = WireValue.Write(Texture),
    };

    /// <summary>Reads one wire entry; lenient — a malformed token yields an empty binding.</summary>
    internal static MaterialTextureBinding FromWire(JToken? token) =>
        token is JObject value
            ? new MaterialTextureBinding
            {
                Name = WireValue.ReadString(value["name"]),
                Texture = WireValue.ReadHandle(value["texture"]),
            }
            : new MaterialTextureBinding();
}
