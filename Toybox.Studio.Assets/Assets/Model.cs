namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The strongly-typed payload of a model asset (the data behind an <c>Asset&lt;Model&gt;</c>) — imported geometry
/// (FBX/OBJ/glTF/…). A model isn't an editable body; the type exists so a model is a first-class
/// <c>Asset&lt;Model&gt;</c> you reference (e.g. a Renderer's model). Its runtime-mirrored fields (stats, material
/// slots, geometry) are a later addition.
/// </summary>
[AssetInfo("fbx", "obj", "gltf", "glb", "dae", "mesh")]
public sealed partial class Model : AssetData
{
}
