using System.Numerics;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Typed, engine-synced view of the <c>box_trigger</c> component.</summary>
[IconAttribute(Icon.Box, Toybox.Studio.Utils.PaletteColor.Cyan)]
public sealed partial class BoxTrigger : Component
{
    [EngineSync] private Vector3 _halfExtents = new(0.5f, 0.5f, 0.5f);

    [EngineSync] private OverlapExecutionMode _overlapExecutionMode = OverlapExecutionMode.Auto;

    [EngineSync] private bool _isOverlapEnabled = true;
}
