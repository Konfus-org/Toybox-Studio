using System.Numerics;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs.Components;

/// <summary>Typed, engine-synced view of the <c>area_light</c> component (a <see cref="Light"/> plus its range and
/// rectangular area size).</summary>
public sealed partial class AreaLight : Light
{
    [EngineSync] private float _range = 10.0f;

    [EngineSync] private Vector2 _areaSize = Vector2.One;
}
