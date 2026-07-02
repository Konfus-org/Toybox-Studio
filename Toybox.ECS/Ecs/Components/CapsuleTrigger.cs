using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Typed, engine-synced view of the <c>capsule_trigger</c> component.</summary>
[IconAttribute(Icon.Pill, Toybox.Studio.Utils.PaletteColor.Cyan)]
public sealed partial class CapsuleTrigger : Component
{
    [EngineSync] private float _radius = 0.5f;

    [EngineSync] private float _halfHeight = 0.5f;

    [EngineSync] private OverlapExecutionMode _overlapExecutionMode = OverlapExecutionMode.Auto;

    [EngineSync] private bool _isOverlapEnabled = true;
}
