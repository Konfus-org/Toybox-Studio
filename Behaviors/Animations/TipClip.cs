using Avalonia.Animation.Easings;
using Avalonia.Animation;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// A rocker-switch lean: rotate toward one side and settle back flat — a one-shot tip rather than a
/// persistent tilt, so the control sits straight at rest. The angle scales with intensity; its sign picks
/// the lean direction (positive = clockwise, the right edge dipping). What a <c>ToggleSwitch</c> does when
/// flipped (<see cref="ToggleTipBehavior"/>).
/// </summary>
public sealed class TipClip : MotionClip
{
    public TipClip() => Duration = TimeSpan.FromMilliseconds(180);

    /// <summary>The peak lean, in degrees; the sign is the direction (positive = clockwise).</summary>
    public double Degrees { get; set; } = 17;

    public override Animation Build(double intensity)
    {
        var peak = Degrees * intensity;
        return new Animation
        {
            Duration = Duration,
            Easing = new SineEaseInOut(),
            FillMode = FillMode.None,
            Children =
            {
                TransformFrame(0d, $"rotate(0deg)"),
                TransformFrame(0.45d, $"rotate({peak}deg)"),
                TransformFrame(1d, $"rotate(0deg)"),
            },
        };
    }
}
