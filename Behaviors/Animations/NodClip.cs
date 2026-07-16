using Avalonia.Animation.Easings;
using Avalonia.Animation;
using Avalonia.Layout;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// The house nod: a quick dip along one axis and back to rest — the one confirmation gesture every corner
/// of the editor shares (a field on Enter, an opening menu, an expanding section, the splash icon when
/// startup completes). <see cref="Depth"/> is in pixels and scales with intensity; its sign picks the
/// direction (positive dips down/right, negative bobs up/left) and <see cref="Orientation"/> the axis.
/// Drives the whole <c>RenderTransform</c> as a <c>TransformOperations</c> and reverts when done
/// (<see cref="FillMode.None"/>), so a control settles back to whatever steady-state transform its styles
/// keep (a hover grow, a rest scale) instead of being pinned to the last keyframe.
/// </summary>
public sealed class NodClip : MotionClip
{
    public NodClip() => Duration = TimeSpan.FromMilliseconds(170);

    /// <summary>The dip in pixels; the sign is the direction (positive = down or right).</summary>
    public double Depth { get; set; } = 3;

    /// <summary>Whether the nod dips vertically (the default) or sideways.</summary>
    public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>
    /// How many dips one run makes. 1 (the default) is the quick confirmation tap; 2 reads as an
    /// emphatic "yes" — the splash icon's bow when loading completes.
    /// </summary>
    public int Dips { get; set; } = 1;

    public override Animation Build(double intensity)
    {
        var dip = Depth * intensity;
        var axis = Orientation == Orientation.Vertical ? "Y" : "X";
        var dips = Math.Max(1, Dips);
        var animation = new Animation
        {
            Duration = Duration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.None,
            Children = { TransformFrame(0d, $"translate{axis}(0px)") },
        };

        // Each dip gets an equal slice of the run: down at 40% of its slice, back to rest at its end.
        for (var i = 0; i < dips; i++)
        {
            animation.Children.Add(TransformFrame((i + 0.4) / dips, $"translate{axis}({dip}px)"));
            animation.Children.Add(TransformFrame((i + 1d) / dips, $"translate{axis}(0px)"));
        }

        return animation;
    }
}
