using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Layout;
using Avalonia.Styling;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// Fades out while sliding away, accelerating like a drop — how the splash content "falls down into" the
/// main window during the startup handoff. Holds its end value (<see cref="FillMode.Forward"/>) so the
/// content never pops back between the fade ending and the window closing. The slide distance scales with
/// intensity; the sign of <see cref="Slide"/> picks the direction (positive = down or right).
/// </summary>
public sealed class SlideFadeClip : MotionClip
{
    public SlideFadeClip() => Duration = TimeSpan.FromMilliseconds(220);

    /// <summary>How far the content slides, in pixels; the sign is the direction (positive = down or right).</summary>
    public double Slide { get; set; } = 14;

    /// <summary>Whether the slide is vertical (the default) or sideways.</summary>
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    public override Animation Build(double intensity)
    {
        var distance = Slide * intensity;
        var axis = Orientation == Orientation.Vertical ? "Y" : "X";
        return new Animation
        {
            Duration = Duration,
            Easing = new CubicEaseIn(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(Visual.RenderTransformProperty, Operations($"translate{axis}(0px)")),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(Visual.RenderTransformProperty, Operations($"translate{axis}({distance}px)")),
                    },
                },
            },
        };
    }
}
