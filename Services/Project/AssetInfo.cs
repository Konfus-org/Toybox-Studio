namespace Toybox.Studio.Services.Project;

/// <summary>
/// Catalog metadata for one asset the engine knows about: a stable id, a display name, the asset's type (its
/// file extension, e.g. "mat"/"png"/"world"), its project-relative path, whether it is a script source a
/// scripting backend recognises (so the "Add Script" picker can list it), and whether it is an engine/bridge
/// built-in (the preview palette the asset viewer draws from). A material additionally carries its
/// render-role <see cref="MaterialType"/> ("raster"/"sky"/"post"/"geo"/"compute"; empty for non-materials)
/// so the editor can treat e.g. a sky material specially. The editor's <see cref="AssetCatalog"/> keeps the
/// live set; pickers and the property grid bind to these. The loaded, editable counterpart is
/// <see cref="Asset"/>.
/// </summary>
public sealed record AssetInfo(
    long Id, string Name, string Type, string Path, bool IsScript = false, bool IsBuiltin = false,
    string MaterialType = "");
