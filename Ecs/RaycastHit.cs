using System.Numerics;

namespace Toybox.Studio.Ecs;

/// <summary>A physics raycast's reply, returned by <see cref="Physics.RaycastAsync"/> — mirroring the
/// engine's <c>RaycastResult</c>. <see cref="Fraction"/> is the hit's distance as a fraction of the
/// query's max distance.</summary>
public readonly record struct RaycastHit(bool HasHit, ulong EntityId, Vector3 Position, float Fraction);
