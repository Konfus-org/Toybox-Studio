using System.Collections.Generic;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Ecs.Components;

/// <summary>
/// Typed, engine-synced view of the <c>lods</c> component: an ordered list of <see cref="Lod"/> bands plus the
/// distance past which the entity stops rendering. Mirrors the engine's serialized <c>Lods</c> fields.
/// </summary>
[IconAttribute(Icon.Layers, PaletteColor.Grey)]
public sealed partial class Lods : Component
{
    [EngineSync] private IReadOnlyList<Lod> _values = [];

    [EngineSync] private float _renderDistance;
}
