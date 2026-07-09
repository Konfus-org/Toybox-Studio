using System.Numerics;

namespace Toybox.Studio.Ecs;

/// <summary>One solid-body collision contact, carried by the <see cref="Collider"/> events —
/// mirroring the engine's collider contact callbacks. <see cref="EntityId"/> is the entity whose
/// collider raised the event; <see cref="Position"/>/<see cref="Normal"/> are world-space at contact
/// begin (zero on end, where only the pair is known).</summary>
public readonly record struct Contact(ulong EntityId, ulong OtherEntityId, Vector3 Position, Vector3 Normal);
