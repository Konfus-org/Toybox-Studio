using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Typed, engine-synced view of the <c>sphere_collider</c> component.</summary>
[IconAttribute(Icon.Circle, Toybox.Studio.Utils.PaletteColor.Green)]
public sealed partial class SphereCollider : Component
{
    [EngineSync] private float _radius = 0.5f;
}
