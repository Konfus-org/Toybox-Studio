using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Toybox.Studio.Services.Motion;

namespace Toybox.Studio.Widgets.Behaviors;

/// <summary>
/// Makes a <see cref="ToggleSwitch"/> tip toward its new state like a physical rocker when flipped, then
/// settle back to flat — a one-shot lean rather than a persistent tilt (the switch sits straight at rest).
/// The tip angle scales with the live <c>AnimationIntensity</c> resource (published by <c>MotionTokens</c>
/// from the Accessibility ▸ Animation intensity setting), so it's subtle by default and nothing at 0.
///
/// Mirrors the <c>PathIcon.spin</c> / <c>TextWiggle</c> approach: it animates the <c>RotateTransform.Angle</c>
/// transform sub-property and runs the animation on the control itself (animating the whole RenderTransform
/// has no keyframe animator, and the animation target must be a Visual). Enabled app-wide via a Style setter
/// on ToggleSwitch in InputStyle.
/// </summary>
public static class ToggleTip
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ToggleSwitch, bool>("Enabled", typeof(ToggleTip));

    public static void SetEnabled(ToggleSwitch toggle, bool value) => toggle.SetValue(EnabledProperty, value);
    public static bool GetEnabled(ToggleSwitch toggle) => toggle.GetValue(EnabledProperty);

    static ToggleTip()
    {
        EnabledProperty.Changed.AddClassHandler<ToggleSwitch>(OnEnabledChanged);
    }

    private static void OnEnabledChanged(ToggleSwitch toggle, AvaloniaPropertyChangedEventArgs args)
    {
        // Detach first so the handlers are wired exactly once regardless of how the flag toggles.
        toggle.IsCheckedChanged -= OnCheckedChanged;
        if (!args.GetNewValue<bool>())
            return;

        toggle.RenderTransformOrigin = RelativePoint.Center;
        toggle.IsCheckedChanged += OnCheckedChanged;
    }

    private static void OnCheckedChanged(object? sender, RoutedEventArgs args)
    {
        if (sender is not ToggleSwitch toggle)
            return;

        var intensity = Intensity(toggle);
        if (intensity <= 0)
            return;

        // Tip toward the new state: clockwise (right edge dips) when turning on, the other way when off.
        var peak = 17 * intensity * (toggle.IsChecked == true ? 1 : -1);
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Easing = new SineEaseInOut(),
            Children =
            {
                Frame(0d, 0),
                Frame(0.45d, peak),
                Frame(1d, 0),
            },
        };

        // Run on the control (a Visual); the animator composes its render transform from the animated
        // RotateTransform.Angle. Fire-and-forget — the keyframes return the angle to 0, so no cleanup is needed.
        _ = animation.RunAsync(toggle);
    }

    private static KeyFrame Frame(double cue, double angle) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(RotateTransform.AngleProperty, angle) },
    };

    private static double Intensity(ToggleSwitch toggle) =>
        toggle.TryFindResource(MotionTokens.IntensityKey, out var value) && value is double intensity ? intensity : 0;
}
