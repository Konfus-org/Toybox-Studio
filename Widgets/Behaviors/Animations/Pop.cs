using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace Toybox.Studio.Widgets.Behaviors.Animations;

/// <summary>
/// Gives a non-button control the same gentle "grow on hover" the buttons have, so world-tree rows and every
/// property row read as the same lively, hoverable surfaces. The grow scales with the live
/// <c>AnimationIntensity</c> resource (published by <c>MotionTokens</c>) and collapses to nothing at 0 — a
/// built-in reduce-motion. It is mild on purpose (rows sit flush against each other, so a big zoom would spill
/// over its neighbours); the directional expand/collapse nod is a separate concern (<see cref="ExpandNod"/>).
///
/// Driven through the control's whole <c>RenderTransform</c> as a <see cref="TransformOperations"/> eased by
/// one <see cref="TransformOperationsTransition"/> the behavior adds — the same mechanism the button hover uses.
/// </summary>
public static class Pop
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(Pop));

    private static readonly TransformOperations Rest = TransformOperations.Parse("scale(1)");

    static Pop()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);
    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.PointerEntered -= OnEntered;
        control.PointerExited -= OnExited;
        if (!args.GetNewValue<bool>())
            return;

        control.RenderTransformOrigin = RelativePoint.Center;
        control.Transitions ??= [];
        if (!control.Transitions.OfType<TransformOperationsTransition>().Any())
            control.Transitions.Add(new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(130),
                Easing = new CubicEaseOut(),
            });

        control.RenderTransform = Rest;
        control.PointerEntered += OnEntered;
        control.PointerExited += OnExited;
    }

    private static void OnEntered(object? sender, PointerEventArgs args)
    {
        if (sender is not Control control)
            return;

        var intensity = Intensity(control);
        control.RenderTransform = intensity <= 0
            ? Rest
            : Parse($"scale({1 + 0.02 * intensity})");
    }

    private static void OnExited(object? sender, PointerEventArgs args)
    {
        if (sender is Control control)
            control.RenderTransform = Rest;
    }

    // TransformOperations.Parse wants CSS-like syntax with an invariant decimal point, so format invariant.
    private static TransformOperations Parse(FormattableString operations) =>
        TransformOperations.Parse(operations.ToString(CultureInfo.InvariantCulture));

    private static double Intensity(Control control) =>
        control.TryFindResource(MotionTokens.IntensityKey, out var value) && value is double intensity ? intensity : 0;
}
