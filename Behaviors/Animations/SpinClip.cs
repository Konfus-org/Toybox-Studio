using Avalonia.Animation.Easings;
using Avalonia.Animation;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// One full 360° pirouette, easing in and out so it winds up and lands rather than snapping. The angle
/// deliberately does NOT scale with intensity — a partial spin reads as broken, so the spin is all or
/// nothing (the player already skips it entirely at intensity 0). The splash icon's occasional flourish.
/// </summary>
public sealed class SpinClip : MotionClip
{
    public SpinClip() => Duration = TimeSpan.FromMilliseconds(700);

    public override Animation Build(double intensity) => new()
    {
        Duration = Duration,
        Easing = new CubicEaseInOut(),
        FillMode = FillMode.None,
        Children =
        {
            TransformFrame(0d, $"rotate(0deg)"),
            TransformFrame(1d, $"rotate(360deg)"),
        },
    };
}
