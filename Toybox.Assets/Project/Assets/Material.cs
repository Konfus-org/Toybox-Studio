using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The strongly-typed payload of a <c>.mat</c> asset (the data behind an <c>Asset&lt;Material&gt;</c>): its render
/// category as a typed, buffered reflected field, over the raw material body (shader, parameters, textures,
/// config) the inspector grid edits and <see cref="Asset.SaveAsync"/> round-trips untouched. Edits accumulate in
/// the body and persist on Save.
/// </summary>
[AssetInfo("mat")]
public sealed partial class Material : AssetData
{
    // The material's render category — wire "type" (the engine field). Unlike the old asset-as-reflected-object
    // shape, this no longer collides with the asset's file-kind string (now Asset.Type on the wrapper), so it
    // needs no `new` hiding.
    [EngineSync] private MaterialType _type;

    [EngineSync] private ShaderProgram _shader = new();

    [EngineSync] private MaterialParameterBindings _parameters = new();

    [EngineSync] private MaterialTextureBindings _textures = new();

    [EngineSync] private MaterialConfig _config = new();
}
