using System.Numerics;

namespace Toybox.Studio.EngineApi.Types;

/// <summary>A mesh's local-space AABB plus enclosing sphere, mirroring the engine's <c>MeshBounds</c>.</summary>
public sealed record MeshBounds(Vector3 Minimum, Vector3 Maximum, Sphere Sphere, bool IsValid);
