using Avalonia.Media;
using System.Numerics;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Components;

/// <summary>An area light source, mirroring the engine's <c>AreaLight</c>.</summary>
public sealed partial class AreaLight : Light
{
    public AreaLight()
    {
        Range = 10.0f;
        AreaSize = Vector2.One;
    }

    public AreaLight(Color color, float intensity = 1.0f, float range = 10.0f, Vector2? areaSize = null)
    {
        Color = color;
        Intensity = intensity;
        Range = range;
        AreaSize = areaSize ?? Vector2.One;
    }

    /// <summary>The effective range in world units.</summary>
    [EngineSync]
    public partial float Range { get; set; }

    /// <summary>The rectangular area size in world units.</summary>
    [EngineSync]
    public partial Vector2 AreaSize { get; set; }
}
