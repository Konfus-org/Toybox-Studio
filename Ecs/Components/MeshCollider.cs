using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Typed, engine-synced view of the <c>mesh_collider</c> component.</summary>
[IconAttribute(Icon.Shapes, Toybox.Studio.Utils.PaletteColor.Green)]
public sealed partial class MeshCollider : Component
{
    [EngineSync] private bool _isConvex = true;
}
