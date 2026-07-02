using Toybox.Studio.Utils;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The render category of a <see cref="Material"/>, mirroring the engine's <c>MaterialType</c> — the integer
/// index the engine serializes under the material's <c>type</c> field. Selects which pipeline the material drives.
/// Each member carries its <see cref="AssetTypeAttribute"/> create-chooser presentation, so the "New Material"
/// menu is built from this enum rather than a parallel list.
/// </summary>
public enum MaterialType
{
    [AssetType("Raster", "Standard surface material", Icon.Palette, PaletteColor.Yellow)]
    Raster = 0,

    [AssetType("Sky", "Environment / background material", Icon.Cloud, PaletteColor.Blue)]
    Sky = 1,

    [AssetType("Post Process", "Full-screen post-process effect", Icon.Sparkles, PaletteColor.Magenta)]
    Post = 2,

    [AssetType("Geometry", "Geometry-stage material", Icon.Box, PaletteColor.Green)]
    Geo = 3,

    [AssetType("Compute", "Compute material", Icon.Cpu, PaletteColor.Cyan)]
    Compute = 4,
}
