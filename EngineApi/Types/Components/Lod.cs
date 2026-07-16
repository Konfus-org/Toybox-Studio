using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Components;

/// <summary>One model to use within a distance band, mirroring the engine's <c>Lod</c>.</summary>
public sealed record Lod
{
    /// <summary>The model asset rendered inside this band.</summary>
    public Handle Handle { get; init; }

    /// <summary>The far edge of this band, in world units.</summary>
    public float MaxDistance { get; init; }
}
