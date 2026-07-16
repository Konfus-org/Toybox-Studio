using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// A shader-stage source asset (<c>.vert</c>/<c>.frag</c>/…), mirrored from the engine's
/// <c>Shader</c>: the stage's source text plus the stage kind from its <c>.meta</c>.
/// </summary>
[AssetExtensions(
    "glsl", "hlsl", "wgsl", "vert", "frag", "geom", "comp", "tesc", "tese", "vsh", "fsh")]
public sealed partial class Shader : Asset
{
    public Shader(ulong id = 0) : base(id) => Source = string.Empty;

    [EngineSync]
    public partial string Source { get; set; }

    [EngineSync]
    public partial ShaderType Type { get; set; }
}
