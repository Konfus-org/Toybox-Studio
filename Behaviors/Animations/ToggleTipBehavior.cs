using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia;

namespace Toybox.Studio.Behaviors.Animations;

/// <summary>
/// Makes a <see cref="ToggleSwitch"/> tip toward its new state like a physical rocker when flipped (a
/// <see cref="TipClip"/> lean-and-settle), so the switch sits straight at rest: clockwise — the right edge
/// dipping — when turning on, the other way when off. Enabled app-wide via a Style setter on ToggleSwitch
/// in InputStyle.
/// </summary>
public static class ToggleTipBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ToggleSwitch, bool>("Enabled", typeof(ToggleTipBehavior));

    private static readonly TipClip TipOn = new();
    private static readonly TipClip TipOff = new() { Degrees = -17 };

    static ToggleTipBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<ToggleSwitch>(OnEnabledChanged);
    }

    public static void SetEnabled(ToggleSwitch toggle, bool value) => toggle.SetValue(EnabledProperty, value);
    public static bool GetEnabled(ToggleSwitch toggle) => toggle.GetValue(EnabledProperty);

    private static void OnEnabledChanged(ToggleSwitch toggle, AvaloniaPropertyChangedEventArgs args)
    {
        // Detach first so the handler is wired exactly once regardless of how the flag toggles.
        toggle.IsCheckedChanged -= OnCheckedChanged;
        if (args.GetNewValue<bool>())
            toggle.IsCheckedChanged += OnCheckedChanged;
    }

    private static void OnCheckedChanged(object? sender, RoutedEventArgs args)
    {
        if (sender is not ToggleSwitch toggle)
            return;

        _ = MotionPlayer.PlayAsync(toggle, toggle.IsChecked == true ? TipOn : TipOff);
    }
}
