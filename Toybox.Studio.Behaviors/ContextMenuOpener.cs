using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Behaviors;

/// <summary>
/// Opens the right context menu on right-click, routed purely by type. Set <c>ContextMenuOpener.Enabled="True"</c>
/// on any control and a right-click (or the context-menu key) builds the menu registered for that control's
/// <c>DataContext</c> and shows it in a flyout at the pointer. The building/showing is delegated to the
/// app-supplied <see cref="Current"/> opener, so this attach-behavior stays in the low UI layer. The handler is
/// on the <b>tunnel</b> route so it fires before the event reaches a descendant editor (a <c>TextBox</c> in a
/// property row), claiming the gesture so the editor's native text menu never preempts the menu.
/// </summary>
public static class ContextMenuOpener
{
    /// <summary>The app-supplied opener that builds + shows menus. Wired once at startup.</summary>
    public static IContextMenuOpener? Current { get; set; }

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(ContextMenuOpener));

    static ContextMenuOpener() => EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.RemoveHandler(InputElement.ContextRequestedEvent, OnContextRequested);
        if (e.NewValue is true)
            control.AddHandler(
                InputElement.ContextRequestedEvent, OnContextRequested,
                RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private static void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: { } dataContext } control
            || Current is not { } opener
            || !opener.Handles(dataContext))
            return;

        // Claim the gesture before the async build so a descendant editor's native menu never also opens.
        e.Handled = true;
        opener.OpenAsync(control, dataContext).FireAndForget();
    }
}
