using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// A material's render-state configuration, mirroring the engine's <c>MaterialConfig</c>. A record
/// value: edit by constructing a changed copy (<c>config with { IsTwoSided = true }</c>) and assigning
/// it back to the owning material, which is what pushes it.
/// </summary>
public sealed record MaterialConfig
{
    public bool IsDepthTestEnabled { get; init; } = true;

    public bool IsDepthWriteEnabled { get; init; } = true;

    public bool IsDepthPrepassEnabled { get; init; }

    public bool IsTwoSided { get; init; }

    public bool IsCullable { get; init; } = true;

    public MaterialDepthFunction DepthFunction { get; init; } = MaterialDepthFunction.Less;

    public MaterialBlendMode BlendMode { get; init; } = MaterialBlendMode.Opaque;

    public ShadowMode ShadowMode { get; init; } = ShadowMode.On;
}
