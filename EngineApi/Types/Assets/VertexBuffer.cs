using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>A mesh's flattened vertex data + its layout, mirroring the engine's <c>VertexBuffer</c>.</summary>
public sealed record VertexBuffer(IReadOnlyList<float> Vertices, VertexBufferLayout Layout);
