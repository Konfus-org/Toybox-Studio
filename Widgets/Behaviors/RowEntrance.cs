using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Toybox.Studio.Services.Motion;

namespace Toybox.Studio.Widgets.Behaviors;

/// <summary>
/// Plays an entrance animation on a control the first time it's attached to the visual tree — used to give
/// every property-grid row some life as a grid is built. By default it's a soft "in" (fade + a small scale-up
/// pop); a control that wants something different supplies its own via <see cref="AnimationProperty"/>. The
/// effect scales with the live <c>AnimationIntensity</c> resource and is skipped entirely at 0 (the row just
/// appears), so it honours the reduce-motion setting.
///
/// Mirrors the <c>PathIcon.spin</c> pattern: the default animation drives <c>Opacity</c> plus the
/// <c>ScaleTransform.ScaleX/Y</c> transform sub-properties and runs on the control itself.
/// </summary>
public static class RowEntrance
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(RowEntrance));

    /// <summary>A custom entrance animation; when unset the default fade + scale-in pop is used.</summary>
    public static readonly AttachedProperty<Animation?> AnimationProperty =
        AvaloniaProperty.RegisterAttached<Control, Animation?>("Animation", typeof(RowEntrance));

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);
    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetAnimation(Control control, Animation? value) => control.SetValue(AnimationProperty, value);
    public static Animation? GetAnimation(Control control) => control.GetValue(AnimationProperty);

    static RowEntrance()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.AttachedToVisualTree -= OnAttached;
        if (args.GetNewValue<bool>())
            control.AttachedToVisualTree += OnAttached;
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is not Control control)
            return;

        var intensity = Intensity(control);
        if (intensity <= 0)
            return;

        control.RenderTransformOrigin = RelativePoint.Center;
        var animation = GetAnimation(control) ?? BuildDefault(intensity);
        _ = animation.RunAsync(control);
    }

    // Fade in from transparent while popping up from a hair below full size — the magnitude of the scale dip
    // scales with intensity so the pop is gentle at the default and punchier near 1.
    private static Animation BuildDefault(double intensity)
    {
        var from = 1 - 0.06 * intensity;
        return new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(ScaleTransform.ScaleXProperty, from),
                        new Setter(ScaleTransform.ScaleYProperty, from),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(ScaleTransform.ScaleXProperty, 1d),
                        new Setter(ScaleTransform.ScaleYProperty, 1d),
                    },
                },
            },
        };
    }

    private static double Intensity(Control control) =>
        control.TryFindResource(MotionTokens.IntensityKey, out var value) && value is double intensity ? intensity : 0;
}
