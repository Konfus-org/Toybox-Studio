using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Toybox.Studio.Services.Motion;

namespace Toybox.Studio.Widgets.Behaviors;

/// <summary>
/// Makes a collapsible item "nod" in the direction it just moved: a small downward dip when it EXPANDS and an
/// upward bob when it COLLAPSES, then settle. Bind <see cref="StateProperty"/> two-way-or-one-way to the item's
/// expanded flag (a section header's <c>IsChecked</c>, a row's <c>ToggleOnTap.State</c>, a tree node's
/// <c>IsExpanded</c>) and every component header / category / struct / world row animates identically.
///
/// Mirrors the <c>PathIcon.spin</c> / <c>EnterNod</c> pattern: a one-shot keyframe animation on the
/// <c>TranslateTransform.Y</c> sub-property, run on the control (a Visual), scaled by the live
/// <c>AnimationIntensity</c> resource and skipped at 0. The first value (initial binding, before the control is
/// loaded) is ignored so a default-expanded item doesn't nod on load — only real toggles animate.
/// </summary>
public static class ExpandNod
{
    public static readonly AttachedProperty<bool> StateProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("State", typeof(ExpandNod));

    public static void SetState(Control control, bool value) => control.SetValue(StateProperty, value);
    public static bool GetState(Control control) => control.GetValue(StateProperty);

    static ExpandNod()
    {
        StateProperty.Changed.AddClassHandler<Control>(OnStateChanged);
    }

    private static void OnStateChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        // The initial binding lands before the row is loaded; only animate real, post-load toggles so a
        // default-expanded section doesn't nod the moment the grid is built.
        if (!control.IsLoaded)
            return;

        Nod(control, expanding: args.GetNewValue<bool>());
    }

    private static void Nod(Control control, bool expanding)
    {
        var intensity = Intensity(control);
        if (intensity <= 0)
            return;

        control.RenderTransformOrigin = RelativePoint.Center;
        // Down (positive Y) when opening, up (negative Y) when closing.
        var dip = (expanding ? 1 : -1) * 4 * intensity;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(170),
            Easing = new CubicEaseOut(),
            Children =
            {
                Frame(0d, 0),
                Frame(0.4d, dip),
                Frame(1d, 0),
            },
        };

        // Run on the control (a Visual); the animator composes its render transform from the animated
        // TranslateTransform.Y and clears back to identity when the nod ends (FillMode.None).
        _ = animation.RunAsync(control);
    }

    private static KeyFrame Frame(double cue, double y) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(TranslateTransform.YProperty, y) },
    };

    private static double Intensity(Control control) =>
        control.TryFindResource(MotionTokens.IntensityKey, out var value) && value is double intensity ? intensity : 0;
}
