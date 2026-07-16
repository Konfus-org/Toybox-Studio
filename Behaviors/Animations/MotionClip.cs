using Avalonia.Animation;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia;
using System.Globalization;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// A reusable, declarative animation recipe: a named motion (nod, wiggle, spin, …) that builds a concrete
/// keyframe <see cref="Animation"/> scaled to the live animation intensity. Clips are plain objects, so a
/// view declares and parameterizes them in XAML (on a <see cref="MotionState"/>) or a trigger behavior holds
/// them in code, and the same motion plays identically everywhere. Always run clips through
/// <see cref="MotionPlayer"/> — it resolves the intensity (skipping the clip entirely at 0, the app's
/// reduce-motion contract), centers the transform origin, and registers the keyframe animator the
/// transform-driving clips rely on.
/// </summary>
public abstract class MotionClip
{
    /// <summary>How long one run of the clip takes (one cycle, when looped by a motion state).</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Builds the animation at the given intensity (0..1; the player never calls this with 0).</summary>
    public abstract Animation Build(double intensity);

    /// <summary>A keyframe driving the whole <c>RenderTransform</c> as a <see cref="TransformOperations"/>.</summary>
    protected static KeyFrame TransformFrame(double cue, FormattableString operations) =>
        Frame(cue, Visual.RenderTransformProperty, Operations(operations));

    /// <summary>A single-setter keyframe.</summary>
    protected static KeyFrame Frame(double cue, AvaloniaProperty property, object? value) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(property, value) },
    };

    // TransformOperations.Parse expects the CSS-like syntax with an invariant decimal point ("1.014"), so
    // every transform is formatted invariant regardless of the user's locale.
    protected static TransformOperations Operations(FormattableString operations) =>
        TransformOperations.Parse(operations.ToString(CultureInfo.InvariantCulture));
}
