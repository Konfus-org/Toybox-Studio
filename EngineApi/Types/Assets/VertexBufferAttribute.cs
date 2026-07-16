using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>One attribute in a vertex buffer layout, mirroring the engine's
/// <c>VertexBufferAttribute</c>.</summary>
public sealed record VertexBufferAttribute(
    string DebugName, VertexFormat Type, uint Offset, bool Normalized);
