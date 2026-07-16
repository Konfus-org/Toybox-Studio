using Avalonia.Media;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Components;

/// <summary>A point light source, mirroring the engine's <c>PointLight</c>.</summary>
public sealed partial class PointLight : Light
{
    public PointLight() => Range = 10.0f;

    public PointLight(Color color, float intensity = 1.0f, float range = 10.0f)
    {
        Color = color;
        Intensity = intensity;
        Range = range;
    }

    /// <summary>The effective range in world units.</summary>
    [EngineSync]
    public partial float Range { get; set; }
}
