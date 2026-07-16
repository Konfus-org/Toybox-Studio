using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>Whether mipmaps are generated for a texture, mirroring the engine's
/// <c>TextureMipmaps</c>.</summary>
public enum TextureMipmaps
{
    Disabled = 0,
    Enabled,
}
