using Toybox.Studio.EngineApi;
using Toybox.Studio.PropertyGrid;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The strongly-typed payload of a <c>.mti</c> asset (the data behind an <c>Asset&lt;MaterialInstance&gt;</c>): a base
/// material plus per-slot overrides. The base <see cref="Material"/> handle is a typed, buffered reflected field
/// (assets reflect on Save); the parameter / texture overrides are edited through the generic inspector grid.
/// (Distinct from the engine's <c>material_instance</c> component-field value.)
/// </summary>
[AssetInfo("mti")]
[IconAttribute(Icon.Palette, Toybox.Studio.Utils.PaletteColor.Magenta)]
public sealed partial class MaterialInstance : AssetData
{
    // The base Material this instance derives from (wire "material"); buffered — folds into the body on Save.
    [DisplayName("Base")]
    [AssetExtensions("mat")]
    [EngineSync] private AssetHandle _material = AssetHandle.None;

    [EngineSync] private MaterialOverrides _overrides = new();
}
