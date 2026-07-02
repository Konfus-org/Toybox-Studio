namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The strongly-typed payload of a shader asset (the data behind an <c>Asset&lt;Shader&gt;</c>) — a GLSL source
/// file for a pipeline stage. Shaders are source, edited in the code editor (not a describable body), so nothing
/// is reflected; the type exists so a shader is a first-class <c>Asset&lt;Shader&gt;</c> whose <see cref="Type"/>
/// (its pipeline stage) can be queried. Authoring (the per-stage scaffold) lives in the <c>AssetFactory</c>.
/// </summary>
[AssetInfo("vert", "frag", "geom", "comp", "tesc", "tese", "glsl", Format = AssetFormat.PlainText)]
public sealed partial class Shader : AssetData
{
    /// <summary>The pipeline stage this shader targets — recovered from its file extension when the handle binds
    /// (a shader carries no body field for it). <see cref="ShaderType.Fragment"/> for an unrecognised extension.</summary>
    public ShaderType Type { get; private set; } = ShaderType.Fragment;

    internal override void OnHandleBound(string extension) =>
        Type = AssetTypeAttribute.ByExtension(extension, ShaderType.Fragment);
}
