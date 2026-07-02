using System;
using System.Collections.Generic;

namespace Toybox.Studio.Project;

/// <summary>
/// Declares, on an asset's data type (an <see cref="Assets.AssetData"/>) or domain subclass, the on-disk file(s)
/// it owns — so the <see cref="AssetFactory"/> routes a file by reflection over these attributes instead of a
/// hard-coded extension map. <see cref="Extensions"/> are the payload extensions (no dot, e.g. <c>"mat"</c>,
/// <c>"png"</c>); <see cref="Format"/> is how the payload is encoded (JSON describe body vs raw text);
/// <see cref="Companions"/> are extra extensions that travel with the asset (deleted/renamed together, e.g. a
/// script's <c>"cpp"</c>) beyond the engine-driven <c>.meta</c> sidecar.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AssetInfoAttribute(params string[] extensions) : Attribute
{
    public IReadOnlyList<string> Extensions { get; } = extensions;

    public AssetFormat Format { get; init; } = AssetFormat.Json;

    public string[] Companions { get; init; } = [];

    /// <summary>Whether authoring/deleting/renaming this kind changes the compiled program, so it triggers a
    /// native rebuild (scripts). Default false.</summary>
    public bool AffectsBuild { get; init; }
}
