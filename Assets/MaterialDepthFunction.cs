namespace Toybox.Studio.Assets;

/// <summary>The depth comparison used when rendering a material, mirroring the engine's
/// <c>MaterialDepthFunction</c>.</summary>
public enum MaterialDepthFunction
{
    Less = 0,
    LessEqual = 1,
    Always = 2,
}
