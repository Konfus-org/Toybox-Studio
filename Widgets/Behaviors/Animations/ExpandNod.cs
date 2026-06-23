using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace Toybox.Studio.Widgets.Behaviors.Animations;

/// <summary>
/// Makes a collapsible item "nod" in the direction it just moved: a small downward dip when it EXPANDS and an
/// upward bob when it COLLAPSES, then settle. Bind <see cref="StateProperty"/> two-way-or-one-way to the item's
/// expanded flag (a section header's <c>IsChecked</c>, a row's <c>ToggleOnTap.State</c>, a tree node's
/// <c>IsExpanded</c>) and every component header / category / struct / world row animates identically.
///
/// A one-shot keyframe animation on the whole <c>RenderTransform</c> as a <see cref="TransformOperations"/>
/// (a vertical <c>translateY</c> dip and back), run on the control (a Visual), scaled by the live
/// <c>AnimationIntensity</c> resource and skipped at 0. It drives the SAME property and transform type as the
/// shared <see cref="Pop"/> hover-grow rather than a <c>TranslateTransform.Y</c> sub-property: every nod-able
/// row also has Pop, which keeps <c>RenderTransform</c> set to a <see cref="TransformOperations"/>, leaving a
/// sub-property animator with no transform to drive — so the nod must speak the same language to play at all.
/// The animation reverts (<see cref="FillMode.None"/>) to Pop's value when it ends, so the row settles back to
/// its hover/rest scale. The first value (initial binding, before the control is loaded) is ignored so a
/// default-expanded item doesn't nod on load — only real toggles animate.
/// </summary>
public static class ExpandNod
{
    public static readonly AttachedProperty<bool> StateProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("State", typeof(ExpandNod));

    static ExpandNod()
    {
        // Pop drives RenderTransform through a TransformOperationsTransition, but a keyframe Animation uses a
        // different path: Animation.InterpretKeyframes consults the animator registry, which in Avalonia 12 has
        // no entry for an ITransform-typed property. Without one, the nod throws "No animator registered for the
        // property RenderTransform" — register our TransformOperations interpolator once, app-wide.
        Animation.RegisterCustomAnimator<ITransform, TransformOperationsAnimator>();
        StateProperty.Changed.AddClassHandler<Control>(OnStateChanged);
    }

    public static void SetState(Control control, bool value) => control.SetValue(StateProperty, value);
    public static bool GetState(Control control) => control.GetValue(StateProperty);

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
            // Revert to the control's own RenderTransform (Pop's hover/rest scale) when the nod finishes, so
            // the row keeps its hover grow rather than being pinned to the last keyframe.
            FillMode = FillMode.None,
            Children =
            {
                Frame(0d, 0),
                Frame(0.4d, dip),
                Frame(1d, 0),
            },
        };

        _ = animation.RunAsync(control);
    }

    // One keyframe driving the whole RenderTransform to a vertical translate (a TransformOperations, matching
    // Pop), so the animator never has to reach into a sub-transform that the Pop-set TransformOperations lacks.
    private static KeyFrame Frame(double cue, double y) => new()
    {
        Cue = new Cue(cue),
        Setters =
        {
            new Setter(
                Visual.RenderTransformProperty,
                TransformOperations.Parse(
                    string.Create(CultureInfo.InvariantCulture, $"translateY({y}px)"))),
        },
    };

    private static double Intensity(Control control) =>
        control.TryFindResource(MotionTokens.IntensityKey, out var value) && value is double intensity ? intensity : 0;
}
