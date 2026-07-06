using Avalonia.Media;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// The common realtime light properties shared by all light types (and the family the shaped lights
/// derive from), mirroring the engine's <c>Light</c>. The renderer normalizes the color so
/// <see cref="Intensity"/> scales total light energy independent of hue.
/// </summary>
public partial class Light : Component
{
    public Light()
    {
        Color = Colors.White;
        Intensity = 1.0f;
        CastShadows = true;
    }

    [EngineSync]
    public partial Color Color { get; set; }

    [EngineSync]
    public partial float Intensity { get; set; }

    [EngineSync]
    public partial bool CastShadows { get; set; }
}
