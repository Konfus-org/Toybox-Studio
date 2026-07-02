using System.Numerics;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Typed, engine-synced view of the <c>box_collider</c> component.</summary>
[IconAttribute(Icon.Box, Toybox.Studio.Utils.PaletteColor.Green)]
public sealed partial class BoxCollider : Component
{
    [EngineSync] private Vector3 _halfExtents = new(0.5f, 0.5f, 0.5f);
}
