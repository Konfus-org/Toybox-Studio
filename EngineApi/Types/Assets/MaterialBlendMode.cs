using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The transparency path used when rendering a material, mirroring the engine's
/// <c>MaterialBlendMode</c>.</summary>
public enum MaterialBlendMode
{
    Opaque = 0,
    AlphaBlend = 1,
    Transparent = 2,
}
