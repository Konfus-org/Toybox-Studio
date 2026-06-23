using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Toybox.Studio.Widgets.Behaviors;

/// <summary>
/// Toggles the expansion of the nearest ancestor <see cref="TreeViewItem"/> when the attached control is
/// tapped — so a world-tree row opens/closes on a single click of its body (selection still happens on the
/// press). Only parents (items that actually have children) toggle; leaves are left alone. Attach with
/// <c>util:ExpandOnTap.Enabled="True"</c> on the row's body, NOT the whole row, so the chevron/delete handle
/// their own taps without double-toggling.
/// </summary>
public static class ExpandOnTap
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(ExpandOnTap));

    static ExpandOnTap()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);
    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.Tapped -= OnTapped;
        if (args.GetNewValue<bool>())
            control.Tapped += OnTapped;
    }

    private static void OnTapped(object? sender, TappedEventArgs args)
    {
        // A Ctrl/Shift tap is a multi-select gesture (see WorldTreeSelection), not an open/close — leave the
        // node's expansion alone so extending the selection across parent rows doesn't fold them.
        if ((args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0)
            return;

        if (sender is Control control
            && control.FindAncestorOfType<TreeViewItem>() is { ItemCount: > 0 } item)
            item.IsExpanded = !item.IsExpanded;
    }
}
