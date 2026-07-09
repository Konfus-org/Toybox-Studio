using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Toybox.Studio.Input;

/// <summary>
/// Attached behavior that turns any control into an input surface: it captures the control's pointer
/// and keyboard input and forwards it — as typed <see cref="InputSnapshot"/>s — to the bound
/// <see cref="IInputSink"/>, which decides where it goes (e.g. an engine viewport streams it to its
/// view's fly camera). Bind the sink to enable:
/// <c>InputBindingBehavior.Sink="{Binding …}"</c>; a null sink detaches. Click to focus so the
/// keyboard drives the surface; <b>Esc</b> releases focus back to the editor.
/// </summary>
public sealed class InputBindingBehavior
{
    /// <summary>Where captured input is forwarded. Setting a sink wires the capture up; null tears it down.</summary>
    public static readonly AttachedProperty<IInputSink?> SinkProperty =
        AvaloniaProperty.RegisterAttached<InputBindingBehavior, Control, IInputSink?>("Sink");

    /// <summary>True while a pointer button is held on the surface — the camera-navigation state the
    /// keybinding dispatcher reads so bare tool keys (Q/W/E/R) never fire mid-flight, when the same
    /// keys are steering the engine camera. Maintained by the capture handler.</summary>
    public static readonly AttachedProperty<bool> IsPointerEngagedProperty =
        AvaloniaProperty.RegisterAttached<InputBindingBehavior, Control, bool>("IsPointerEngaged");

    // Keeps the per-control handler (and its event subscriptions) alive for the control's lifetime.
    private static readonly AttachedProperty<Handler?> HandlerProperty =
        AvaloniaProperty.RegisterAttached<InputBindingBehavior, Control, Handler?>("Handler");

    static InputBindingBehavior() =>
        SinkProperty.Changed.AddClassHandler<Control>(OnSinkChanged);

    public static void SetSink(Control control, IInputSink? value) => control.SetValue(SinkProperty, value);

    public static IInputSink? GetSink(Control control) => control.GetValue(SinkProperty);

    public static bool GetIsPointerEngaged(Control control) => control.GetValue(IsPointerEngagedProperty);

    private static void OnSinkChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.GetValue(HandlerProperty)?.Detach();
        control.SetValue(
            HandlerProperty,
            args.GetNewValue<IInputSink?>() is { } sink ? new Handler(control, sink) : null);
    }

    /// <summary>Holds the input state for one control and translates Avalonia events into snapshots.</summary>
    private sealed class Handler
    {
        private readonly Control _control;
        private readonly IInputSink _sink;
        private readonly HashSet<Key> _keys = [];
        private Point _lastPointer;
        private Point _pointer;
        private bool _hasPointer;
        private HashSet<MouseButton> _buttons = [];

        public Handler(Control control, IInputSink sink)
        {
            _control = control;
            _sink = sink;
            control.Focusable = true;
            control.PointerPressed += OnPointerPressed;
            control.PointerReleased += OnPointerReleased;
            control.PointerMoved += OnPointerMoved;
            control.PointerWheelChanged += OnPointerWheel;
            control.KeyDown += OnKeyDown;
            control.KeyUp += OnKeyUp;
            control.GotFocus += OnFocusChanged;
            control.LostFocus += OnLostFocus;
        }

        public void Detach()
        {
            _control.PointerPressed -= OnPointerPressed;
            _control.PointerReleased -= OnPointerReleased;
            _control.PointerMoved -= OnPointerMoved;
            _control.PointerWheelChanged -= OnPointerWheel;
            _control.KeyDown -= OnKeyDown;
            _control.KeyUp -= OnKeyUp;
            _control.GotFocus -= OnFocusChanged;
            _control.LostFocus -= OnLostFocus;
        }

        private void Send(double dx, double dy, double wheel)
        {
            // The engaged flag tracks the held-button state every send refreshes, so it can never lag
            // the snapshots the engine sees.
            _control.SetValue(IsPointerEngagedProperty, _buttons.Count > 0);
            _sink.ForwardInput(new InputSnapshot(
                _control.IsFocused, [.. _keys], [.. _buttons],
                _pointer, new Vector(dx, dy), wheel, _control.Bounds.Size));
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Click to focus so the keyboard drives this surface; capture so look-drag keeps receiving
            // moves even past the control's bounds.
            _control.Focus();
            var point = e.GetCurrentPoint(_control);
            _buttons = ButtonsOf(point.Properties);
            _lastPointer = point.Position;
            _pointer = point.Position;
            _hasPointer = true;
            e.Pointer.Capture(_control);
            Send(0, 0, 0);
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            var point = e.GetCurrentPoint(_control);
            _buttons = ButtonsOf(point.Properties);
            if (_buttons.Count == 0)
                e.Pointer.Capture(null);
            Send(0, 0, 0);
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            var point = e.GetCurrentPoint(_control);
            _buttons = ButtonsOf(point.Properties);
            _pointer = point.Position;

            // Ignore hover movement when the surface isn't engaged, to avoid streaming idle deltas.
            if (!_control.IsFocused && _buttons.Count == 0)
            {
                _lastPointer = point.Position;
                return;
            }

            var dx = _hasPointer ? point.Position.X - _lastPointer.X : 0;
            var dy = _hasPointer ? point.Position.Y - _lastPointer.Y : 0;
            _lastPointer = point.Position;
            _hasPointer = true;
            Send(dx, dy, 0);
        }

        private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
        {
            if (_control.IsFocused || _buttons.Count != 0)
                Send(0, 0, e.Delta.Y);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            // Esc releases the surface's focus so input stops forwarding; it is never forwarded as input.
            if (e.Key == Key.Escape)
            {
                ReleaseFocus();
                e.Handled = true;
                return;
            }

            if (_keys.Add(e.Key))
            {
                Send(0, 0, 0);
                e.Handled = true;
            }
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (_keys.Remove(e.Key))
            {
                Send(0, 0, 0);
                e.Handled = true;
            }
        }

        private void OnFocusChanged(object? sender, RoutedEventArgs e) => Send(0, 0, 0);

        private void OnLostFocus(object? sender, RoutedEventArgs e)
        {
            // Releasing focus drops all input so the app stops receiving it.
            _buttons.Clear();
            _keys.Clear();
            Send(0, 0, 0);
        }

        // Moves keyboard focus off the surface (Esc) so input stops forwarding.
        private void ReleaseFocus()
        {
            if (TopLevel.GetTopLevel(_control) is { } top)
                top.Focus();
        }

        private static HashSet<MouseButton> ButtonsOf(PointerPointProperties properties)
        {
            HashSet<MouseButton> buttons = [];
            if (properties.IsLeftButtonPressed)
                buttons.Add(MouseButton.Left);
            if (properties.IsRightButtonPressed)
                buttons.Add(MouseButton.Right);
            if (properties.IsMiddleButtonPressed)
                buttons.Add(MouseButton.Middle);
            return buttons;
        }
    }
}
