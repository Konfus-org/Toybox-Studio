using Avalonia.Animation;
using Avalonia.Animation.Easings;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// A tiny, fast horizontal shake — out, across to the other side, and back to center. The typing "juice"
/// under <see cref="TextWiggleBehavior"/>; one run is short enough to pulse per keystroke and settle within
/// a beat of the last one. The offset scales with intensity.
/// </summary>
public sealed class WiggleClip : MotionClip
{
    public WiggleClip() => Duration = TimeSpan.FromMilliseconds(110);

    /// <summary>The peak sideways offset, in pixels.</summary>
    public double Amplitude { get; set; } = 1.5;

    public override Animation Build(double intensity)
    {
        var offset = Amplitude * intensity;
        return new Animation
        {
            Duration = Duration,
            Easing = new SineEaseInOut(),
            FillMode = FillMode.None,
            Children =
            {
                TransformFrame(0d, $"translateX(0px)"),
                TransformFrame(0.35d, $"translateX({offset}px)"),
                TransformFrame(0.7d, $"translateX({-offset}px)"),
                TransformFrame(1d, $"translateX(0px)"),
            },
        };
    }
}
