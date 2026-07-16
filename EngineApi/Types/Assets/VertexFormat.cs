using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The component layout of one vertex attribute, mirroring the engine's <c>VertexFormat</c>.</summary>
public enum VertexFormat
{
    Float = 0,
    Vec2,
    Vec3,
    Vec4,
    Uint32,
    Int32,
}
