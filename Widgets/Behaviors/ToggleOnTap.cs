using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Toybox.Studio.Widgets.Behaviors;

/// <summary>
/// Click-anywhere expand toggle for a property-grid parent row. Bind <see cref="StateProperty"/> two-way to
/// the row's expanded flag and set <see cref="EnabledProperty"/> true; a tap anywhere on the row flips it.
/// Interactive children (e.g. a list's add button) handle their own taps, so they act without toggling.
/// This is purely the functional toggle — the press/hover "juice" comes from the shared <see cref="Pop"/>
/// behavior on the same row, so every lively item animates identically and the two never fight one transform.
/// </summary>
public static class ToggleOnTap
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(ToggleOnTap));

    public static readonly AttachedProperty<bool> StateProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("State", typeof(ToggleOnTap));

    static ToggleOnTap()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);
    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetState(Control control, bool value) => control.SetValue(StateProperty, value);
    public static bool GetState(Control control) => control.GetValue(StateProperty);

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.Tapped -= OnTapped;
        if (args.GetNewValue<bool>())
            control.Tapped += OnTapped;
    }

    private static void OnTapped(object? sender, TappedEventArgs args)
    {
        if (sender is Control control)
            SetState(control, !GetState(control));
    }
}
