using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// An image asset (<c>.png</c>/<c>.jpg</c>/…), mirrored from the engine's <c>Texture</c>: the sampling
/// and surface settings that live in its <c>.meta</c>, plus the decoded <see cref="Pixels"/> and
/// <see cref="Resolution"/>. The image reader sources the pixels from the file (they aren't written back
/// to it), but the engine serializes them, so the editor mirrors the full texture.
/// </summary>
[AssetExtensions("png", "jpg", "jpeg", "bmp", "tga", "dds", "hdr", "exr", "ktx", "ktx2")]
public sealed partial class Texture : Asset
{
    public Texture(ulong id = 0) : base(id)
    {
        Wrap = TextureWrap.Repeat;
        Filter = TextureFilter.Linear;
        Format = TextureFormat.Rgb;
        Mipmaps = TextureMipmaps.Enabled;
        Compression = TextureCompression.Disabled;
        Pixels = [];
        Resolution = new Size(1, 1);
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

    /// <summary>The decoded pixel payload (the engine's <c>Texture::pixels</c>). Engine → studio only:
    /// the image reader fills it from the source file.</summary>
    [EngineSync(Mode = SyncMode.OneWayFromEngine, Converter = typeof(ByteListConverter))]
    public partial IReadOnlyList<byte> Pixels { get; private set; }

    /// <summary>The pixel dimensions (the engine's <c>Texture::resolution</c>). Engine → studio only.</summary>
    [EngineSync(Mode = SyncMode.OneWayFromEngine, Converter = typeof(SizeConverter))]
    public partial Size Resolution { get; private set; }
}
