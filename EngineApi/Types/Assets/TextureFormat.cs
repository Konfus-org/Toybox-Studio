using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The pixel format, mirroring the engine's <c>TextureFormat</c>.</summary>
public enum TextureFormat
{
    Rgb = 0,
    Rgba,
    Rgba8,
    Rgba16Float,
    Rgba32Float,
    R8,
    R16Float,
    Rg8,
    Rg16Float,
    Depth24Stencil8,
    Depth32Float,
}
