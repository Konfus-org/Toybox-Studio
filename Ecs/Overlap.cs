namespace Toybox.Studio.Ecs;

/// <summary>One trigger overlap relationship, carried by the <see cref="Trigger"/> events — mirroring
/// the engine's <c>ColliderOverlapEvent</c>.</summary>
public readonly record struct Overlap(ulong TriggerEntityId, ulong OtherEntityId);
