using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.Project.Assets;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Ecs.Components;

/// <summary>
/// Typed, engine-synced view of the <c>material_instance</c> component: a base <see cref="Material"/> asset plus
/// the per-slot <see cref="Overrides"/> layered onto it. It carries exactly the two fields the engine's
/// <c>material_instance</c> describe body does, so the inspector sources it from reflection like every other typed
/// component; ComponentViewModel gives it its base-aware special editor (Base picker + slot overrides) on top.
/// (Distinct from the asset <c>MaterialInstance</c> in <c>Project.Assets</c>, which is the <c>.mti</c> payload —
/// this is the engine's component-field value.)
/// </summary>
[IconAttribute(Icon.Palette, PaletteColor.Magenta)]
public sealed partial class MaterialInstance : Component
{
    // The base Material this instance derives from (wire "material").
    [AssetExtensions("mat")]
    [EngineSync] private AssetHandle _material = AssetHandle.None;

    [EngineSync] private MaterialOverrides _overrides = new();
}
