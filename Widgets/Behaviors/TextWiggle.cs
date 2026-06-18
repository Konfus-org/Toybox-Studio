using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Toybox.Studio.Services.Motion;

namespace Toybox.Studio.Widgets.Behaviors;

/// <summary>
/// Gives a typed-into field a tiny, fast horizontal wiggle on each keystroke — tactile "juice" while typing.
/// The wiggle moves the VISIBLE FIELD, not the text inside it: by default the control that owns the inner text
/// box (its templated parent — e.g. the NumericUpDown well) or, lacking one, the text box itself; a composite
/// control whose text box isn't a template child (the SearchBox pill) points <see cref="TargetProperty"/> at
/// the visual to wiggle instead. Only one wiggle runs per target at a time (keystrokes during a wiggle are
/// ignored), so it pulses at a steady cadence while typing and settles within one short cycle once typing
/// stops. Magnitude scales with the live <c>AnimationIntensity</c> resource (nothing at 0).
///
/// Mirrors the <c>PathIcon.spin</c> pattern: it animates the <c>TranslateTransform.X</c> transform sub-property
/// and runs the animation on the target Visual (animating the whole RenderTransform has no keyframe animator,
/// and the RunAsync target must be a Visual). Enabled app-wide via a Style setter on TextBox in InputStyle.
/// </summary>
public static class TextWiggle
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Enabled", typeof(TextWiggle));

    /// <summary>The visual to wiggle instead of the auto-resolved field (e.g. the SearchBox pill).</summary>
    public static readonly AttachedProperty<Control?> TargetProperty =
        AvaloniaProperty.RegisterAttached<TextBox, Control?>("Target", typeof(TextWiggle));

    public static void SetEnabled(TextBox box, bool value) => box.SetValue(EnabledProperty, value);
    public static bool GetEnabled(TextBox box) => box.GetValue(EnabledProperty);
    public static void SetTarget(TextBox box, Control? value) => box.SetValue(TargetProperty, value);
    public static Control? GetTarget(TextBox box) => box.GetValue(TargetProperty);

    // The targets with a wiggle currently in flight — a keystroke that lands while one is running is ignored,
    // so the motion pulses at a steady cadence rather than piling up, and stops within one cycle after typing.
    private static readonly HashSet<Visual> Animating = [];

    static TextWiggle()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>(OnEnabledChanged);
    }

    private static void OnEnabledChanged(TextBox box, AvaloniaPropertyChangedEventArgs args)
    {
        // Detach first so the handler is wired exactly once regardless of how the flag toggles.
        box.TextChanged -= OnTextChanged;
        if (args.GetNewValue<bool>())
            box.TextChanged += OnTextChanged;
    }

    private static void OnTextChanged(object? sender, TextChangedEventArgs args)
    {
        if (sender is not TextBox box)
            return;

        var intensity = Intensity(box);
        if (intensity <= 0)
            return;

        Wiggle(ResolveTarget(box), intensity);
    }

    // The visible field to move: an explicit Target wins; otherwise the control whose template the text box is
    // part of (so a NumericUpDown's well wiggles, not its inner text); otherwise the text box is itself the
    // field. Shared with EnterNod so the confirmation nod lands on the same visual the wiggle does.
    internal static Visual ResolveTarget(TextBox box) =>
        GetTarget(box) ?? box.TemplatedParent as Control ?? box;

    private static async void Wiggle(Visual target, double intensity)
    {
        if (!Animating.Add(target))
            return;

        target.RenderTransformOrigin = RelativePoint.Center;
        var offset = 1.5 * intensity; // px
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(110),
            Easing = new SineEaseInOut(),
            Children =
            {
                Frame(0d, 0),
                Frame(0.35d, offset),
                Frame(0.7d, -offset),
                Frame(1d, 0),
            },
        };

        try
        {
            // Run on the target (a Visual); the animator composes its render transform from the animated
            // TranslateTransform.X. The keyframes return X to 0, so the field is left flat.
            await animation.RunAsync(target);
        }
        catch
        {
            // A control torn down mid-wiggle just stops; nothing to clean up.
        }
        finally
        {
            Animating.Remove(target);
        }
    }

    private static KeyFrame Frame(double cue, double x) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(TranslateTransform.XProperty, x) },
    };

    private static double Intensity(TextBox box) =>
        box.TryFindResource(MotionTokens.IntensityKey, out var value) && value is double intensity ? intensity : 0;
}
