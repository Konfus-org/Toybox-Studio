using System.Numerics;
using Toybox.Studio.EngineApi.Types;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>A node in a model's part hierarchy — a mesh drawn under a transform with a material slot,
/// mirroring the engine's <c>ModelPart</c>.</summary>
public sealed record ModelPart(
    Matrix4x4 Transform, uint MeshIndex, uint MaterialIndex, IReadOnlyList<uint> Children);
