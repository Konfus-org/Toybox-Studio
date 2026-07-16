using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>Whether a texture is GPU-compressed on load, mirroring the engine's
/// <c>TextureCompression</c>.</summary>
public enum TextureCompression
{
    Disabled = 0,
    Auto,
}
