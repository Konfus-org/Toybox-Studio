
namespace Toybox.Studio.Project.Assets;

/// <summary>Serialized mirror of the engine's <c>MaterialTextureBinding</c> — one texture bound to a shader sampler
/// by name. A plain value type.</summary>
public sealed class MaterialTextureBinding
{
    public string Name { get; set; } = "";

    [AssetExtensions("png", "jpg", "jpeg", "tga", "bmp")]
    public AssetHandle Texture { get; set; } = AssetHandle.None;
}
