using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// The file extensions (no dot) an asset payload type is recognized from — mirroring the engine's
/// <c>[[tbx::extension]]</c> tags for the type. Imported kinds (models, textures, audio, shader source)
/// declare their full recognized set here; an authored body asset's single creation extension rides on
/// <see cref="CreatableAttribute"/> instead (and is also treated as recognized). Read via reflection by
/// <see cref="AssetKinds"/> so extension→type knowledge lives on the asset classes, not duplicated by
/// each consumer.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AssetExtensionsAttribute(params string[] extensions) : Attribute
{
    /// <summary>The recognized file extensions (no dot).</summary>
    public IReadOnlyList<string> Extensions { get; } = extensions;
}
