using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// Attached behavior that captures pointer and keyboard input on a viewport surface and forwards it to
/// the view-model's <see cref="IViewportInputSink"/>, which relays it to the engine. Editor viewports
/// drive the fly camera; the game view forwards raw input to the running game and owns the focus model
/// (<b>Esc</b> stops the game, <b>Alt+Esc</b> releases focus back to the editor). Setting
/// <c>ViewportInput.Enabled="True"</c> wires it up so the view-models stay free of code-behind routing.
/// </summary>
public sealed class ViewportInput
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ViewportInput, Control, bool>("Enabled");

    // Keeps the per-control handler (and its event subscriptions) alive for the control's lifetime.
    private static readonly AttachedProperty<Handler?> HandlerProperty =
        AvaloniaProperty.RegisterAttached<ViewportInput, Control, Handler?>("Handler");

    static ViewportInput() =>
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        control.GetValue(HandlerProperty)?.Detach();
        control.SetValue(HandlerProperty, args.GetNewValue<bool>() ? new Handler(control) : null);
    }

    /// <summary>Holds the input state for one control and translates Avalonia events into sink calls.</summary>
    private sealed class Handler
    {
        private readonly Control _control;
        private readonly HashSet<int> _scancodes = [];
        private Point _lastPointer;
        private Point _pointer;
        private bool _hasPointer;
        private int _buttons;
        private int _moveKeys;
        private bool _suppressNextMove; // True for the single PointerMoved our own re-centre warp triggers, so it isn't read as a delta.

        public Handler(Control control)
        {
            _control = control;
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

        private IViewportInputSink? Sink => _control.DataContext as IViewportInputSink;

        private void Send(double dx, double dy, double wheel)
        {
            Sink?.ForwardInput(new ViewportInputPayload(
                _control.IsFocused, _buttons, _moveKeys, _scancodes.ToArray(),
                _pointer.X, _pointer.Y, dx, dy, wheel));
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // Click to focus so the keyboard drives this viewport; capture so look-drag keeps receiving
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
            _buttons = ButtonsOf(e.GetCurrentPoint(_control).Properties);
            if (_buttons == 0)
                e.Pointer.Capture(null);
            Send(0, 0, 0);
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            var point = e.GetCurrentPoint(_control);
            _buttons = ButtonsOf(point.Properties);
            _pointer = point.Position;

            // Consume the synthetic move produced by our own re-centre warp without emitting a delta.
            if (_suppressNextMove)
            {
                _suppressNextMove = false;
                _lastPointer = point.Position;
                return;
            }

            // Ignore hover movement when the viewport isn't engaged, to avoid streaming idle deltas.
            if (!_control.IsFocused && _buttons == 0)
            {
                _lastPointer = point.Position;
                return;
            }

            var dx = _hasPointer ? point.Position.X - _lastPointer.X : 0;
            var dy = _hasPointer ? point.Position.Y - _lastPointer.Y : 0;
            _lastPointer = point.Position;
            _hasPointer = true;
            Send(dx, dy, 0);

            // Mouselook: pin the OS cursor to the panel centre so look-deltas keep flowing without the
            // (hidden) pointer drifting out of the panel or hitting a screen edge.
            if (Sink is { RelativeMouse: true } && _control.IsFocused)
                RecenterCursor();
        }

        // Warps the OS cursor back to the panel centre (Avalonia has no cursor-warp API, so go through
        // the platform). Marks the resulting move to be ignored and rebases the delta origin to centre.
        private void RecenterCursor()
        {
            var bounds = _control.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            var center = new Point(bounds.Width / 2, bounds.Height / 2);
            var screen = _control.PointToScreen(center);
            if (SetCursorPos(screen.X, screen.Y))
            {
                _suppressNextMove = true;
                _lastPointer = center;
            }
        }

        private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
        {
            if (_control.IsFocused || _buttons != 0)
                Send(0, 0, e.Delta.Y);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            // Game view focus model: Alt+Esc releases focus back to the editor; Esc stops the game.
            // Neither is forwarded to the game.
            if (Sink is { IsGame: true } gameSink && e.Key == Key.Escape)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                    ReleaseFocus();
                else
                    gameSink.StopGame();
                e.Handled = true;
                return;
            }

            var changed = false;
            if (TryMoveBit(e.Key, out var bit))
            {
                _moveKeys |= bit;
                changed = true;
            }

            var scancode = SdlScancodes.Map(e.Key);
            if (scancode != 0)
                changed |= _scancodes.Add(scancode);

            if (changed)
            {
                Send(0, 0, 0);
                e.Handled = true;
            }
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            var changed = false;
            if (TryMoveBit(e.Key, out var bit))
            {
                _moveKeys &= ~bit;
                changed = true;
            }

            var scancode = SdlScancodes.Map(e.Key);
            if (scancode != 0)
                changed |= _scancodes.Remove(scancode);

            if (changed)
            {
                Send(0, 0, 0);
                e.Handled = true;
            }
        }

        private void OnFocusChanged(object? sender, RoutedEventArgs e) => Send(0, 0, 0);

        private void OnLostFocus(object? sender, RoutedEventArgs e)
        {
            // Releasing focus drops all input so the engine camera/game stops receiving it.
            _buttons = 0;
            _moveKeys = 0;
            _scancodes.Clear();
            Send(0, 0, 0);
        }

        // Moves keyboard focus off the viewport (Alt+Esc) so input stops forwarding to the game.
        private void ReleaseFocus()
        {
            if (TopLevel.GetTopLevel(_control) is { } top)
                top.Focus();
        }

        // Win32 cursor warp. The editor is Windows-only; Avalonia exposes no cross-platform equivalent.
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        private static int ButtonsOf(PointerPointProperties properties) =>
            (properties.IsLeftButtonPressed ? 0x1 : 0)
            | (properties.IsRightButtonPressed ? 0x2 : 0)
            | (properties.IsMiddleButtonPressed ? 0x4 : 0);

        private static bool TryMoveBit(Key key, out int bit)
        {
            bit = key switch
            {
                Key.W => 0x01,
                Key.S => 0x02,
                Key.A => 0x04,
                Key.D => 0x08,
                Key.E => 0x10,
                Key.Q => 0x20,
                _ => 0,
            };
            return bit != 0;
        }
    }
}
