namespace Toybox.Studio.Services.Project;

/// <summary>
/// One asset the engine knows about: a stable id, a display name, the asset's type (its file extension,
/// e.g. "mat"/"png"/"world"), and its project-relative path. The editor's
/// <see cref="AssetCatalog"/> keeps the live set; pickers and the property grid bind to these.
/// </summary>
public sealed record Asset(long Id, string Name, string Type, string Path);
