using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>One mesh's geometry (vertex buffer + index buffer + bounds), mirroring the engine's
/// <c>Mesh</c>.</summary>
public sealed record Mesh(VertexBuffer Vertices, IReadOnlyList<uint> Indices, MeshBounds Bounds);
