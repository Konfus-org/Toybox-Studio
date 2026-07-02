using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Project.Assets;

/// <summary>How a texture samples outside [0,1]. Mirrors the engine <c>TextureWrap</c> (serialized index).</summary>
public enum TextureWrap
{
    ClampToEdge = 0,
    MirroredRepeat = 1,
    Repeat = 2,
}

/// <summary>Texture min/mag filtering. Mirrors the engine <c>TextureFilter</c>.</summary>
public enum TextureFilter
{
    Nearest = 0,
    Linear = 1,
}

/// <summary>Texture pixel format. Mirrors the engine <c>TextureFormat</c>.</summary>
public enum TextureFormat
{
    Rgb = 0,
    Rgba = 1,
    Rgba8 = 2,
    Rgba16Float = 3,
    Rgba32Float = 4,
    R8 = 5,
    R16Float = 6,
    Rg8 = 7,
    Rg16Float = 8,
    Depth24Stencil8 = 9,
    Depth32Float = 10,
}

/// <summary>Whether mipmaps are generated. Mirrors the engine <c>TextureMipmaps</c>.</summary>
public enum TextureMipmaps
{
    Disabled = 0,
    Enabled = 1,
}

/// <summary>Texture compression mode. Mirrors the engine <c>TextureCompression</c>.</summary>
public enum TextureCompression
{
    Disabled = 0,
    Auto = 1,
}

/// <summary>
/// The strongly-typed payload of a texture asset (the data behind an <c>Asset&lt;Texture&gt;</c>): its <c>.meta</c>
/// import settings, modeled as typed, buffered reflected fields — assets reflect on Save (not live), so edits
/// accumulate and persist through <see cref="Asset.SaveAsync"/>.
/// </summary>
[AssetInfo("png", "jpg", "jpeg", "bmp", "tga", "dds", "hdr", "exr", "ktx", "ktx2")]
public sealed partial class Texture : AssetData
{
    [EngineSync] private TextureWrap _wrap = TextureWrap.Repeat;

    [EngineSync] private TextureFilter _filter = TextureFilter.Linear;

    [EngineSync] private TextureFormat _format = TextureFormat.Rgb;

    [EngineSync] private TextureMipmaps _mipmaps = TextureMipmaps.Enabled;

    [EngineSync] private TextureCompression _compression = TextureCompression.Disabled;
}
