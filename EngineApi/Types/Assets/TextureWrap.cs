using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>How sampling outside [0,1] UVs behaves, mirroring the engine's <c>TextureWrap</c>.</summary>
public enum TextureWrap
{
    ClampToEdge = 0,
    MirroredRepeat,
    Repeat,
}
