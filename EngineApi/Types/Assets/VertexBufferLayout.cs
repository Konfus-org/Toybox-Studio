using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>A vertex buffer's attribute layout + stride, mirroring the engine's
/// <c>VertexBufferLayout</c>.</summary>
public sealed record VertexBufferLayout(IReadOnlyList<VertexBufferAttribute> Elements, uint Stride);
