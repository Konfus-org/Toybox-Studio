using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Typed, engine-synced view of the <c>capsule_collider</c> component.</summary>
[IconAttribute(Icon.Pill, Toybox.Studio.Utils.PaletteColor.Green)]
public sealed partial class CapsuleCollider : Component
{
    [EngineSync] private float _radius = 0.5f;

    [EngineSync] private float _halfHeight = 0.5f;
}
