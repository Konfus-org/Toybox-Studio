using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The shader stage a <see cref="Shader"/> asset holds source for, mirroring the engine's
/// <c>ShaderType</c>.</summary>
public enum ShaderType
{
    None = 0,
    Vertex,
    Tesselation,
    Geometry,
    Fragment,
    Compute,
}
