namespace Toybox.Studio.Project.Assets;

/// <summary>The depth comparison function a material renders with. Mirrors the engine <c>MaterialDepthFunction</c>.</summary>
public enum MaterialDepthFunction
{
    Less = 0,
    LessEqual = 1,
    Always = 2,
}

/// <summary>The transparency path a material renders with. Mirrors the engine <c>MaterialBlendMode</c>.</summary>
public enum MaterialBlendMode
{
    Opaque = 0,
    AlphaBlend = 1,
    Transparent = 2,
}

/// <summary>How a material participates in realtime shadowing. Mirrors the engine <c>ShadowMode</c>.</summary>
public enum ShadowMode
{
    Off = 0,
    On = 1,
}

/// <summary>
/// Serialized mirror of the engine's <c>MaterialConfig</c> — a material's render-state flags (depth/blend/cull/
/// shadow). A plain value type nested in <see cref="Material"/>.
/// </summary>
public sealed class MaterialConfig
{
    public bool IsDepthTestEnabled { get; set; } = true;

    public bool IsDepthWriteEnabled { get; set; } = true;

    public bool IsDepthPrepassEnabled { get; set; }

    public bool IsTwoSided { get; set; }

    public bool IsCullable { get; set; } = true;

    public MaterialDepthFunction DepthFunction { get; set; } = MaterialDepthFunction.Less;

    public MaterialBlendMode BlendMode { get; set; } = MaterialBlendMode.Opaque;

    public ShadowMode ShadowMode { get; set; } = ShadowMode.On;
}
