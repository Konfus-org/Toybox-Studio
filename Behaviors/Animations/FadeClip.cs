using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// A plain opacity fade that HOLDS its end value (<see cref="FillMode.Forward"/>) — for taking something
/// off screen (or bringing it on) rather than a there-and-back gesture. The fade is binary by nature, so it
/// doesn't scale with intensity; at 0 the player skips it and the caller's functional follow-up (closing a
/// window, collapsing a panel) simply happens instantly.
/// </summary>
public sealed class FadeClip : MotionClip
{
    public FadeClip() => Duration = TimeSpan.FromMilliseconds(220);

    public double From { get; set; } = 1;
    public double To { get; set; }

    public override Animation Build(double intensity) => new()
    {
        Duration = Duration,
        Easing = new SineEaseInOut(),
        FillMode = FillMode.Forward,
        Children =
        {
            Frame(0d, Visual.OpacityProperty, From),
            Frame(1d, Visual.OpacityProperty, To),
        },
    };
}
