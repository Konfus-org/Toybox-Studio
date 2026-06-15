using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Makes a <see cref="NumericUpDown"/> draggable like a modern editor number field: press and drag
/// horizontally to scrub the value, or click without dragging to type as usual. A small movement threshold
/// disambiguates the two, so the text caret still works. Hold Shift to scrub ×10, Ctrl for fine ×0.1.
///
/// Attach with <c>local:NumericScrub.Enabled="True"</c>; tune the per-pixel step with
/// <c>local:NumericScrub.Step</c> (defaults to 0.1). Handlers are tunnelled so the inner text box does not
/// start a selection once a scrub begins.
/// </summary>
public static class NumericScrub
{
    private const double DragThreshold = 4.0;

    private static readonly ConditionalWeakTable<NumericUpDown, DragState> States = new();

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, bool>("Enabled", typeof(NumericScrub));

    public static readonly AttachedProperty<decimal> StepProperty =
        AvaloniaProperty.RegisterAttached<NumericUpDown, decimal>(
            "Step", typeof(NumericScrub), defaultValue: 0.1m);

    static NumericScrub()
    {
        EnabledProperty.Changed.AddClassHandler<NumericUpDown>(OnEnabledChanged);
    }

    public static void SetEnabled(NumericUpDown element, bool value) =>
        element.SetValue(EnabledProperty, value);

    public static bool GetEnabled(NumericUpDown element) => element.GetValue(EnabledProperty);

    public static void SetStep(NumericUpDown element, decimal value) =>
        element.SetValue(StepProperty, value);

    public static decimal GetStep(NumericUpDown element) => element.GetValue(StepProperty);

    private static void OnEnabledChanged(NumericUpDown control, AvaloniaPropertyChangedEventArgs args)
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
        if (sender is not NumericUpDown control || !control.IsEnabled)
            return;
        if (!args.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        // Arm but don't capture yet: a press that never moves stays a plain click that focuses for typing.
        var state = States.GetValue(control, _ => new DragState());
        state.Armed = true;
        state.Scrubbing = false;
        state.StartX = args.GetPosition(control).X;
        state.LastX = state.StartX;
        state.StartValue = control.Value ?? 0m;
    }

    private static void OnMoved(object? sender, PointerEventArgs args)
    {
        if (sender is not NumericUpDown control || !States.TryGetValue(control, out var state) || !state.Armed)
            return;

        var x = args.GetPosition(control).X;
        if (!state.Scrubbing)
        {
            if (Math.Abs(x - state.StartX) < DragThreshold)
                return;

            state.Scrubbing = true;
            state.LastX = x;
            args.Pointer.Capture(control);
            control.Cursor = new Cursor(StandardCursorType.SizeWestEast);
        }

        var step = GetStep(control);
        if (step <= 0m)
            step = control.Increment;

        var speed = 1.0;
        if (args.KeyModifiers.HasFlag(KeyModifiers.Shift))
            speed = 10.0;
        else if (args.KeyModifiers.HasFlag(KeyModifiers.Control))
            speed = 0.1;

        var delta = (decimal)((x - state.LastX) * speed) * step;
        state.LastX = x;

        var next = (control.Value ?? state.StartValue) + delta;
        next = Math.Clamp(next, control.Minimum, control.Maximum);
        control.Value = next;
        args.Handled = true;
    }

    private static void OnReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (sender is not NumericUpDown control || !States.TryGetValue(control, out var state))
            return;

        if (state.Scrubbing)
        {
            args.Pointer.Capture(null);
            control.Cursor = Cursor.Default;
            args.Handled = true;
        }

        state.Armed = false;
        state.Scrubbing = false;
    }

    private sealed class DragState
    {
        public bool Armed;
        public bool Scrubbing;
        public double StartX;
        public double LastX;
        public decimal StartValue;
    }
}
