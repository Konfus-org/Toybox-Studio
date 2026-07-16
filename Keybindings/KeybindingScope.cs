using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia;

namespace Toybox.Studio.Keybindings;

/// <summary>
/// Attached behavior that routes a window's key-downs through a <see cref="KeybindingDispatcher"/>:
/// <c>KeybindingScope.Dispatcher="{Binding …}"</c> on the main window (a null dispatcher detaches).
/// The handler tunnels, so it sees the chord before any focused control (the viewport's input capture
/// included) can swallow it; a matched binding marks the event handled.
/// </summary>
public sealed class KeybindingScope
{
    /// <summary>The dispatcher the control's key-downs run through. Setting it wires the tunneling
    /// handler up; null tears it down.</summary>
    public static readonly AttachedProperty<KeybindingDispatcher?> DispatcherProperty =
        AvaloniaProperty.RegisterAttached<KeybindingScope, Control, KeybindingDispatcher?>("Dispatcher");

    static KeybindingScope() =>
        DispatcherProperty.Changed.AddClassHandler<Control>(OnDispatcherChanged);

    public static void SetDispatcher(Control control, KeybindingDispatcher? value) =>
        control.SetValue(DispatcherProperty, value);

    public static KeybindingDispatcher? GetDispatcher(Control control) =>
        control.GetValue(DispatcherProperty);

    private static void OnDispatcherChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        control.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        if (args.GetNewValue<KeybindingDispatcher?>() is not null)
        {
            control.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            // Key-up is only needed to track the released snap-hold key; it never matches a chord.
            control.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        }
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is Control control && GetDispatcher(control) is { } dispatcher)
        {
            dispatcher.TrackSnapHold(e, isDown: true);
            e.Handled = e.Handled || dispatcher.TryHandle(e);
        }
    }

    private static void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is Control control && GetDispatcher(control) is { } dispatcher)
            dispatcher.TrackSnapHold(e, isDown: false);
    }
}
