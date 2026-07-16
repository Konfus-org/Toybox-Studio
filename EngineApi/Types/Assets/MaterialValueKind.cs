using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// The typed shape of a <see cref="MaterialValue"/> — the shader-parameter variants the editor renders
/// with a matching value editor. <see cref="Unknown"/> covers a wire shape the typed kinds don't model
/// (a matrix), which the editor shows read-only so it still round-trips.
/// </summary>
public enum MaterialValueKind
{
    Bool,
    Int,
    Float,
    Vector2,
    Vector3,
    Vector4,
    Color,
    Unknown,
}
