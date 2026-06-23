using System.Globalization;
using Avalonia;
using Avalonia.Media.Transformation;

namespace Toybox.Studio.Widgets.Behaviors.Animations;

/// <summary>
/// Publishes the live "juice" motion tokens into the application resources, so the static XAML transitions
/// (button press/hover in ButtonStyle, toggle tilt in InputStyle) and the <c>TextWiggle</c> behavior all read
/// their animation magnitude from one place. The magnitude is driven by the Accessibility ▸ Animation
/// intensity editor setting (0..1) — published at startup, on Save, and live while the slider is dragged.
/// At intensity 0 every transform collapses to identity, so motion turns fully off (a built-in "reduce
/// motion"). Theme-independent: motion magnitude is an accessibility preference, not a theme trait.
/// </summary>
public static class MotionTokens
{
    /// <summary>The application-resource key carrying the raw 0..1 intensity (read by the wiggle behavior).</summary>
    public const string IntensityKey = "AnimationIntensity";

    /// <summary>
    /// Writes the intensity-scaled motion tokens (the rest/hover/press button transforms, the toggle tilt
    /// transforms, and the raw intensity) into the live application resources. No-op before the app exists.
    /// </summary>
    public static void Publish(double intensity)
    {
        if (Application.Current is not { } app)
            return;

        var i = Math.Clamp(intensity, 0, 1);
        var resources = app.Resources;
        resources[IntensityKey] = i;
        resources["ThemeControlRestTransform"] = TransformOperations.Parse("scale(1)");
        resources["ThemeButtonHoverTransform"] = Transform($"scale({1 + 0.04 * i})");
        resources["ThemeButtonPressTransform"] = Transform($"scale({1 - 0.07 * i})");
        // Section-header bands are full-width and sit flush against their neighbours, so they get a much milder
        // hover grow than a free-standing button — barely a nudge, kept small enough to stay within the item.
        resources["ThemeHeaderHoverTransform"] = Transform($"scale({1 + 0.012 * i})");
        // The toggle "tip" is a one-shot reactive animation driven by the ToggleTip behavior (which reads the
        // intensity directly), not a steady-state transform — so no toggle tokens are published here.
    }

    // TransformOperations.Parse expects the CSS-like syntax with an invariant decimal point ("1.014"), so the
    // numbers must be formatted invariant regardless of the user's locale.
    private static TransformOperations Transform(FormattableString transform) =>
        TransformOperations.Parse(transform.ToString(CultureInfo.InvariantCulture));
}
