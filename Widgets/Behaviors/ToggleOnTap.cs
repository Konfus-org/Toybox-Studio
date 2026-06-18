using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace Toybox.Studio.Widgets.Behaviors;

/// <summary>
/// Click-anywhere expand toggle for a property-grid parent row. Bind <see cref="StateProperty"/> two-way to
/// the row's expanded flag and set <see cref="EnabledProperty"/> true; a tap anywhere on the row flips it,
/// with the same little shrink-then-grow press pulse the section-header ToggleButtons get for free.
/// Interactive children (e.g. a list's add button) handle their own taps, so they act without toggling.
/// </summary>
public static class ToggleOnTap
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(ToggleOnTap));

    public static readonly AttachedProperty<bool> StateProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("State", typeof(ToggleOnTap));

    // Shrink to 97% and back, matching the press feedback FluentTheme gives its buttons. Animated through the
    // ScaleTransform.ScaleX/Y SUB-properties (which have a keyframe double animator) run on the row itself (a
    // Visual) — the animator composes the row's RenderTransform from them. Animating the whole RenderTransform
    // instead throws "No animator registered for the property RenderTransform"; running on a bare transform
    // throws because the animation target must be a Visual.
    private static readonly Animation PressPulse = new()
    {
        Duration = TimeSpan.FromMilliseconds(150),
        Easing = new QuadraticEaseInOut(),
        Children =
        {
            Pulse(0d, 1.0),
            Pulse(0.5d, 0.97),
            Pulse(1d, 1.0),
        },
    };

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);
    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetState(Control control, bool value) => control.SetValue(StateProperty, value);
    public static bool GetState(Control control) => control.GetValue(StateProperty);

    static ToggleOnTap()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.Tapped -= OnTapped;
        if (args.GetNewValue<bool>())
            control.Tapped += OnTapped;
    }

    private static void OnTapped(object? sender, TappedEventArgs args)
    {
        if (sender is not Control control)
            return;

        SetState(control, !GetState(control));

        // Run on the control (a Visual) so the animation clock resolves; the scale keyframes drive its
        // RenderTransform and clear back to identity when the pulse ends (FillMode.None).
        control.RenderTransformOrigin = RelativePoint.Center;
        PressPulse.RunAsync(control);
    }

    private static KeyFrame Pulse(double cue, double factor) => new()
    {
        Cue = new Cue(cue),
        Setters =
        {
            new Setter(ScaleTransform.ScaleXProperty, factor),
            new Setter(ScaleTransform.ScaleYProperty, factor),
        },
    };
}
