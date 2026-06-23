using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Toybox.Studio.Widgets.Toolbar;

/// <summary>
/// Turns the toolbar's grip into a drag-to-dock gesture: drag it toward any edge of the hosting viewport and
/// the toolbar snaps to that edge (its orientation flips to match). Attach
/// <c>local:ToolbarDockDrag.Enabled="True"</c> on the grip; its DataContext is the
/// <see cref="ToolbarViewModel"/>. The host region is the visual parent the
/// <see cref="ToolbarView"/> is overlaid on (the viewport surface).
/// </summary>
public static class ToolbarDockDrag
{
    private const double DragThreshold = 4.0;

    private static readonly ConditionalWeakTable<Control, DragState> States = new();

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(ToolbarDockDrag));

    static ToolbarDockDrag()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    public static void SetEnabled(Control element, bool value) => element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(Control element) => element.GetValue(EnabledProperty);

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            control.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel);
        }
        else
        {
            control.RemoveHandler(InputElement.PointerPressedEvent, OnPressed);
            control.RemoveHandler(InputElement.PointerMovedEvent, OnMoved);
            control.RemoveHandler(InputElement.PointerReleasedEvent, OnReleased);
            States.Remove(control);
        }
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Control handle || !handle.IsEnabled)
            return;
        if (!args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            return;

        if (handle.DataContext is not ToolbarViewModel toolbar
            || handle.FindAncestorOfType<ToolbarView>() is not { } view
            || view.GetVisualParent() is not Visual host)
            return;

        var state = States.GetValue(handle, _ => new DragState());
        state.Armed = true;
        state.Dragging = false;
        state.Toolbar = toolbar;
        state.View = view;
        state.Host = host;
        state.Start = args.GetPosition(host);
    }

    private static void OnMoved(object? sender, PointerEventArgs args)
    {
        if (sender is not Control handle || !States.TryGetValue(handle, out var state) || !state.Armed)
            return;
        if (state.Host is null)
            return;

        if (!state.Dragging)
        {
            var position = args.GetPosition(state.Host);
            if (Math.Abs(position.X - state.Start.X) < DragThreshold
                && Math.Abs(position.Y - state.Start.Y) < DragThreshold)
                return;

            state.Dragging = true;
            args.Pointer.Capture(handle);
            handle.Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        // Surface the dock targets and light up the edge the toolbar would snap to from here.
        var pointer = args.GetPosition(state.Host);
        DockDragIndicator.Show(state.Host, NearestEdge(pointer, state.Host.Bounds.Size));

        // Let the whole toolbar follow the pointer while dragging (offset from its docked rest position by
        // the drag delta), so it feels picked up and carried rather than jumping only on release.
        if (state.View is not null)
            state.View.RenderTransform =
                new TranslateTransform(pointer.X - state.Start.X, pointer.Y - state.Start.Y);

        args.Handled = true;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is not Control handle || !States.TryGetValue(handle, out var state))
            return;

        if (state.Dragging && state.Host is not null && state.Toolbar is not null)
        {
            state.Toolbar.SetDockedEdge(NearestEdge(args.GetPosition(state.Host), state.Host.Bounds.Size));
            args.Pointer.Capture(null);
            handle.Cursor = Cursor.Default;
            args.Handled = true;
        }

        // Drop the drag-follow offset: the toolbar snaps to the chosen edge via its alignment bindings.
        if (state.View is not null)
            state.View.RenderTransform = null;

        DockDragIndicator.Clear();
        state.Armed = false;
        state.Dragging = false;
        state.Toolbar = null;
        state.View = null;
        state.Host = null;
    }

    // The edge the pointer is closest to within the host bounds.
    private static ToolbarEdge NearestEdge(Point pointer, Size host)
    {
        var edge = ToolbarEdge.Top;
        var best = pointer.Y;

        var bottom = host.Height - pointer.Y;
        if (bottom < best)
        {
            best = bottom;
            edge = ToolbarEdge.Bottom;
        }

        if (pointer.X < best)
        {
            best = pointer.X;
            edge = ToolbarEdge.Left;
        }

        var right = host.Width - pointer.X;
        if (right < best)
            edge = ToolbarEdge.Right;

        return edge;
    }

    private sealed class DragState
    {
        public bool Armed;
        public bool Dragging;
        public ToolbarViewModel? Toolbar;
        public ToolbarView? View;
        public Visual? Host;
        public Point Start;
    }
}
