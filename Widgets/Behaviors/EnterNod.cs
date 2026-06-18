using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Toybox.Studio.Services.Motion;

namespace Toybox.Studio.Widgets.Behaviors;

/// <summary>
/// On Enter, a single-line field gives a small vertical "nod" (a downward dip and back) to confirm the input
/// was accepted, then drops focus so the edit commits and the field is no longer active. Multi-line fields
/// (<c>AcceptsReturn</c>) are left alone — there Enter inserts a newline. The nod lands on the same visible
/// field the wiggle uses (<see cref="TextWiggle.ResolveTarget"/>, so the SearchBox pill / NumericUpDown well
/// nods, not the inner text) and scales with the live <c>AnimationIntensity</c> resource; the focus drop
/// happens regardless of intensity (it's functional, not decorative).
///
/// Mirrors the <c>PathIcon.spin</c> pattern: animates the <c>TranslateTransform.Y</c> sub-property and runs on
/// the target Visual. Enabled app-wide via a Style setter on TextBox in InputStyle.
/// </summary>
public static class EnterNod
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("Enabled", typeof(EnterNod));

    public static void SetEnabled(TextBox box, bool value) => box.SetValue(EnabledProperty, value);
    public static bool GetEnabled(TextBox box) => box.GetValue(EnabledProperty);

    static EnterNod()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>(OnEnabledChanged);
    }

    private static void OnEnabledChanged(TextBox box, AvaloniaPropertyChangedEventArgs args)
    {
        // Detach first so the handler is wired exactly once regardless of how the flag toggles.
        box.KeyDown -= OnKeyDown;
        if (args.GetNewValue<bool>())
            box.KeyDown += OnKeyDown;
    }

    private static void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (sender is not TextBox box || args.Key != Key.Enter || box.AcceptsReturn)
            return;

        Nod(box);

        // Commit + deactivate: move focus off the field to the window root, which pushes any LostFocus-bound
        // text and leaves the field inactive. (This Avalonia build's IFocusManager has no ClearFocus, so we
        // redirect focus to the top level — made focusable — instead.)
        if (TopLevel.GetTopLevel(box) is { } top)
        {
            top.Focusable = true;
            top.Focus();
        }
    }

    private static void Nod(TextBox box)
    {
        var intensity = Intensity(box);
        if (intensity <= 0)
            return;

        var target = TextWiggle.ResolveTarget(box);
        target.RenderTransformOrigin = RelativePoint.Center;
        var dip = 2.5 * intensity; // px downward
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(160),
            Easing = new CubicEaseOut(),
            Children =
            {
                Frame(0d, 0),
                Frame(0.4d, dip),
                Frame(1d, 0),
            },
        };

        // Run on the target (a Visual); the animator composes its render transform from the animated
        // TranslateTransform.Y. The keyframes return Y to 0, so the field is left flat.
        _ = animation.RunAsync(target);
    }

    private static KeyFrame Frame(double cue, double y) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(TranslateTransform.YProperty, y) },
    };

    private static double Intensity(Visual visual) =>
        visual.TryFindResource(MotionTokens.IntensityKey, out var value) && value is double intensity ? intensity : 0;
}
