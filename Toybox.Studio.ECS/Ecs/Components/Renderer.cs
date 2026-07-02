using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Ecs.Components;

/// <summary>
/// Typed, engine-synced view of the <c>renderer</c> component: a <see cref="Model"/> asset plus per-slot
/// <see cref="Materials"/> overrides (each a material/instance asset handle, aligned 1:1 with the model's slots;
/// an empty handle inherits the model's own). Edits push live via <c>reflect.set</c>.
/// </summary>
[IconAttribute(Icon.Cuboid, PaletteColor.Blue)]
public sealed partial class Renderer : Component
{
    [EngineSync] private AssetHandle _model = AssetHandle.None;

    [EngineSync] private IReadOnlyList<AssetHandle> _materials = [];
}
