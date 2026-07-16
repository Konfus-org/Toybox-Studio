using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Toybox.Studio.NodeGraph;

/// <summary>
/// Drives an overlay node by its header (or a plug): a press-and-move shifts the node's saved offset (see
/// <see cref="EntityNodeViewModel.Drag"/>) so the node moves and the move persists, while a press-and-release
/// without moving is a click that activates it (<see cref="EntityNodeViewModel.Activate"/> — a plug click
/// selects and opens the entity). Attached to the open card's header only (so the embedded inspector isn't
/// hijacked) and to the whole plug square. Deltas are in overlay pixels (the node canvas has no zoom), taken
/// against the top level so they're reference-stable.
/// </summary>
public static class NodeDragBehavior
{
    // Below this cumulative movement (overlay px) a press-release counts as a click, not a drag.
    private const double DragThreshold = 5.0;

    private sealed class DragState
    {
        public bool Dragging;
        public Point Last;
        public double Distance;
    }

    private static readonly ConditionalWeakTable<Control, DragState> States = [];

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(NodeDragBehavior));

    static NodeDragBehavior() => EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);
    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.PointerPressed -= OnPressed;
        control.PointerMoved -= OnMoved;
        control.PointerReleased -= OnReleased;
        if (args.GetNewValue<bool>())
        {
            control.PointerPressed += OnPressed;
            control.PointerMoved += OnMoved;
            control.PointerReleased += OnReleased;
        }
    }

    private static void OnPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Control control || !args.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        var state = States.GetOrCreateValue(control);
        state.Dragging = true;
        state.Distance = 0;
        state.Last = args.GetPosition(null);
        args.Pointer.Capture(control);
        args.Handled = true;
    }

    private static void OnMoved(object? sender, PointerEventArgs args)
    {
        if (sender is not Control control
            || !States.TryGetValue(control, out var state) || !state.Dragging
            || control.DataContext is not EntityNodeViewModel node)
            return;

        var position = args.GetPosition(null);
        var dx = position.X - state.Last.X;
        var dy = position.Y - state.Last.Y;
        state.Distance += Math.Abs(dx) + Math.Abs(dy);
        // Ignore sub-threshold jitter so a click doesn't nudge the node; drag once past it.
        if (state.Distance >= DragThreshold)
            node.Drag(dx, dy);
        state.Last = position;
        args.Handled = true;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is not Control control || !States.TryGetValue(control, out var state) || !state.Dragging)
            return;

        state.Dragging = false;
        args.Pointer.Capture(null);
        // A press-release that never became a drag is a click: activate (select / open) the node.
        if (state.Distance < DragThreshold && control.DataContext is EntityNodeViewModel node)
            node.Activate();
    }
}
