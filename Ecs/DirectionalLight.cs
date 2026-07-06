using Avalonia.Media;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// A directional light source, mirroring the engine's <c>DirectionalLight</c>. The renderer averages
/// directional light colors and scales that color by the sum of (<see cref="Ambient"/> ×
/// <see cref="Light.Intensity"/>) across directional lights.
/// </summary>
public sealed partial class DirectionalLight : Light
{
    public DirectionalLight() => Ambient = 0.03f;

    public DirectionalLight(Color color, float intensity = 1.0f, float ambient = 0.03f)
    {
        Color = color;
        Intensity = intensity;
        Ambient = ambient;
    }

    /// <summary>The ambient lighting contribution this directional light produces.</summary>
    [EngineSync]
    public partial float Ambient { get; set; }
}
