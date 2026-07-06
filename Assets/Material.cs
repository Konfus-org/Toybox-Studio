using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Assets;

/// <summary>
/// A <c>.mat</c> asset, mirrored from the engine's <c>Material</c>: the shader program, default
/// parameter and texture bindings, and render config a surface is drawn with. The nested values are
/// records — edit by assigning a changed copy back to its property, which is what pushes it.
/// </summary>
[Creatable("mat")]
public sealed partial class Material : Asset
{
    /// <summary>The load of the existing material with the given id (see <see cref="Asset.Loaded"/>).</summary>
    public Material(ulong id = 0) : base(id) => InitializeDefaults();

    /// <summary>A fresh material, authored at the given location by its first save.</summary>
    public Material(string name, string directory = "") : base(name, directory) => InitializeDefaults();

    [EngineSync]
    public partial MaterialType Type { get; set; }

    [EngineSync(Converter = typeof(ShaderProgramConverter))]
    public partial ShaderProgram Shader { get; set; }

    [EngineSync(Converter = typeof(MaterialParameterBindingsConverter))]
    public partial MaterialParameterBindings Parameters { get; set; }

    [EngineSync(Converter = typeof(MaterialTextureBindingsConverter))]
    public partial MaterialTextureBindings Textures { get; set; }

    [EngineSync(Converter = typeof(MaterialConfigConverter))]
    public partial MaterialConfig Config { get; set; }

    private void InitializeDefaults()
    {
        Shader = new ShaderProgram();
        Parameters = new MaterialParameterBindings();
        Textures = new MaterialTextureBindings();
        Config = new MaterialConfig();
    }
}
