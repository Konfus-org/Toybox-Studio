using System.Numerics;

namespace Toybox.Studio.EngineApi.Types;

/// <summary>A bounding sphere (center + radius), mirroring the engine's <c>Sphere</c>.</summary>
public sealed record Sphere(Vector3 Center, float Radius);
