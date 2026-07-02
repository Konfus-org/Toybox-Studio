using Toybox.Studio.Utils;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The pipeline stage of a <see cref="Shader"/> source — the one thing that distinguishes one shader file from
/// another. A shader isn't keyed by a body field (it's plain GLSL source); its stage IS its file extension, so
/// each member carries that <see cref="AssetTypeAttribute.Extension"/> (and its create-chooser presentation). The
/// "New Shader" menu is built from this enum, and <see cref="Shader.Type"/> recovers a shader's stage from its
/// extension — the enum is the single source of truth for both.
/// </summary>
public enum ShaderType
{
    [AssetType("Vertex", "Vertex stage (.vert)", Icon.Sparkles, PaletteColor.Magenta, Extension = "vert")]
    Vertex,

    [AssetType("Fragment", "Fragment / pixel stage (.frag)", Icon.Sparkles, PaletteColor.Magenta, Extension = "frag")]
    Fragment,

    [AssetType("Geometry", "Geometry stage (.geom)", Icon.Sparkles, PaletteColor.Magenta, Extension = "geom")]
    Geometry,

    [AssetType("Compute", "Compute stage (.comp)", Icon.Sparkles, PaletteColor.Cyan, Extension = "comp")]
    Compute,

    [AssetType("Tess Control", "Tessellation control (.tesc)", Icon.Sparkles, PaletteColor.Magenta, Extension = "tesc")]
    TessControl,

    [AssetType("Tess Eval", "Tessellation evaluation (.tese)", Icon.Sparkles, PaletteColor.Magenta, Extension = "tese")]
    TessEval,

    [AssetType("Include", "Shared GLSL snippet (.glsl)", Icon.FileCode, PaletteColor.Grey, Extension = "glsl")]
    Include,
}
