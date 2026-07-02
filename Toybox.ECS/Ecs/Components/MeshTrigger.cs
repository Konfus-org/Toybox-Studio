using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Typed, engine-synced view of the <c>mesh_trigger</c> component.</summary>
[IconAttribute(Icon.Shapes, Toybox.Studio.Utils.PaletteColor.Cyan)]
public sealed partial class MeshTrigger : Component
{
    [EngineSync] private bool _isConvex = true;

    [EngineSync] private OverlapExecutionMode _overlapExecutionMode = OverlapExecutionMode.Auto;

    [EngineSync] private bool _isOverlapEnabled = true;
}
