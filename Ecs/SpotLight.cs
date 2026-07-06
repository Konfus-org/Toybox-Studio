using Avalonia.Media;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>A spot light source, mirroring the engine's <c>SpotLight</c>.</summary>
public sealed partial class SpotLight : Light
{
    public SpotLight()
    {
        Range = 10.0f;
        InnerAngle = 20.0f;
        OuterAngle = 35.0f;
    }

    public SpotLight(
        Color color,
        float intensity = 1.0f,
        float range = 10.0f,
        float innerAngle = 20.0f,
        float outerAngle = 35.0f)
    {
        Color = color;
        Intensity = intensity;
        Range = range;
        InnerAngle = innerAngle;
        OuterAngle = outerAngle;
    }

    /// <summary>The effective range in world units.</summary>
    [EngineSync]
    public partial float Range { get; set; }

    /// <summary>The inner spotlight angle in degrees.</summary>
    [EngineSync]
    public partial float InnerAngle { get; set; }

    /// <summary>The outer spotlight angle in degrees.</summary>
    [EngineSync]
    public partial float OuterAngle { get; set; }
}
