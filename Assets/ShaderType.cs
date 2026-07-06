namespace Toybox.Studio.Assets;

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
