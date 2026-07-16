using Avalonia.Animation.Easings;
using Avalonia.Animation;
using Avalonia.Styling;
using Avalonia;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// A soft "in" for a control that just appeared: fade up from transparent while popping from a hair below
/// full size — the entrance the property-grid rows use as a grid is built. The scale dip scales with
/// intensity, so the pop is gentle at the default setting and punchier near 1.
/// </summary>
public sealed class EntranceClip : MotionClip
{
    public EntranceClip() => Duration = TimeSpan.FromMilliseconds(180);

    /// <summary>How far below full size the pop starts, as a fraction of the control's size.</summary>
    public double Grow { get; set; } = 0.06;

    public override Animation Build(double intensity)
    {
        var from = 1 - Grow * intensity;
        return new Animation
        {
            Duration = Duration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.None,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(Visual.RenderTransformProperty, Operations($"scale({from})")),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(Visual.RenderTransformProperty, Operations($"scale(1)")),
                    },
                },
            },
        };
    }
}
