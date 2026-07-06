using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Assets;

/// <summary>
/// One row of the <see cref="AssetCatalog"/> — an asset the engine's registry knows, as advertised by
/// <see cref="EngineCommands.EditorListAssets"/>. <see cref="Type"/> is the file-extension token the
/// engine resolves the asset's registered type from ("mat", "png", …); <see cref="Path"/> is the
/// engine-normalized project-relative path. Built-in entries (engine/bridge preview assets) carry no
/// project file, and a material row also advertises its render-role (<see cref="MaterialType"/>) so
/// pickers can treat sky materials specially.
/// </summary>
public sealed record AssetEntry(
    ulong Id,
    string Name,
    string Type,
    string Path,
    bool IsScript,
    bool IsBuiltin,
    bool HasMeta,
    string? MaterialType = null)
{
    /// <summary>The entry as an engine handle: the id is the identity, the path the resolvable label.</summary>
    public Handle Handle => new(Path, Id);
}
