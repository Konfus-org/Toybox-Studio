using System.Collections.Generic;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// Serialized mirror of the engine's <c>ShaderProgram</c> — the explicit per-stage shader asset handles a
/// material's pipeline is built from. A plain value type nested in <see cref="Material"/>.
/// </summary>
public sealed class ShaderProgram
{
    [AssetExtensions("vert", "glsl")]
    public AssetHandle Vertex { get; set; } = AssetHandle.None;

    [AssetExtensions("frag", "glsl")]
    public AssetHandle Fragment { get; set; } = AssetHandle.None;

    [AssetExtensions("tesc", "tese", "glsl")]
    public AssetHandle Tesselation { get; set; } = AssetHandle.None;

    [AssetExtensions("geom", "glsl")]
    public AssetHandle Geometry { get; set; } = AssetHandle.None;

    [AssetExtensions("comp", "glsl")]
    public IReadOnlyList<AssetHandle> Computes { get; set; } = [];
}
