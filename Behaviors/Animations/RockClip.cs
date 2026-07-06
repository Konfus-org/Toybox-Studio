using Avalonia.Animation;
using Avalonia.Animation.Easings;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// A gentle rocking: tilt one way, swing through to the other, and return to upright — a toy teetering on
/// its box. One run is a single cycle that starts and ends at rest, so a looping <see cref="MotionState"/>
/// replays it seamlessly and can always hand off to the next clip from upright. The splash icon idles with
/// this while the editor loads. The tilt scales with intensity.
/// </summary>
public sealed class RockClip : MotionClip
{
    public RockClip() => Duration = TimeSpan.FromMilliseconds(1600);

    /// <summary>The peak tilt to each side, in degrees.</summary>
    public double Degrees { get; set; } = 6;

    public override Animation Build(double intensity)
    {
        var tilt = Degrees * intensity;
        return new Animation
        {
            Duration = Duration,
            Easing = new SineEaseInOut(),
            FillMode = FillMode.None,
            Children =
            {
                TransformFrame(0d, $"rotate(0deg)"),
                TransformFrame(0.25d, $"rotate({tilt}deg)"),
                TransformFrame(0.75d, $"rotate({-tilt}deg)"),
                TransformFrame(1d, $"rotate(0deg)"),
            },
        };
    }
}
