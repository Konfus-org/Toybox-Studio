using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Assets;

/// <summary>
/// An image asset (<c>.png</c>/<c>.jpg</c>/…), mirrored from the engine's <c>Texture</c>: the sampling
/// and surface settings that live in its <c>.meta</c>. The pixel payload and resolution are
/// runtime-only on the engine side (loaded from the source image, never serialized), so they are not
/// mirrored.
/// </summary>
public sealed partial class Texture : Asset
{
    public Texture(ulong id = 0) : base(id)
    {
        Wrap = TextureWrap.Repeat;
        Filter = TextureFilter.Linear;
        Format = TextureFormat.Rgb;
        Mipmaps = TextureMipmaps.Enabled;
        Compression = TextureCompression.Disabled;
    }

    [EngineSync]
    public partial TextureWrap Wrap { get; set; }

    [EngineSync]
    public partial TextureFilter Filter { get; set; }

    [EngineSync]
    public partial TextureFormat Format { get; set; }

    [EngineSync]
    public partial TextureMipmaps Mipmaps { get; set; }

    [EngineSync]
    public partial TextureCompression Compression { get; set; }
}
