using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Toybox.Studio.Services.Commands;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Widgets.ContextMenu;
using Toybox.Studio.Widgets.Toolbar;

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
        private readonly HashSet<InputKey> _keys = [];
        private Point _lastPointer;
        private Point _pointer;
        private bool _hasPointer;
        private int _buttons;
        private int _moveKeys;
        private bool _suppressNextMove; // True for the single PointerMoved our own re-centre warp triggers, so it isn't read as a delta.

        // A left press becomes a pick on release if it didn't turn into a drag (camera look/pan use right/middle,
        // so the left button is free for selection). The press records its spot + Shift state; movement past a
        // small slop cancels the pick so a future left-drag (the marquee) isn't read as a click.
        private const double ClickSlopSquared = 16; // ~4px
        private Point _pressPosition;
        private bool _pendingPick;
        private bool _pickAdditive;
        // True once a left-drag has grown past the slop into a rubber-band marquee selection.
        private bool _marqueeActive;

        // A right press becomes a context-menu open on release if it stayed put (right-drag is the camera pan,
        // so a right-tap is otherwise free). The menu acts on the current selection, like the world tree.
        private bool _rightTapCandidate;
        private Point _rightPressPosition;

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

        private IViewportInputSink? Sink => _control.DataContext as IViewportInputSink;

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
            var bounds = _control.Bounds;
            Sink?.ForwardInput(new ViewportInputPayload(
                _control.IsFocused, _buttons, _moveKeys, _keys.ToArray(),
                _pointer.X, _pointer.Y, dx, dy, wheel, bounds.Width, bounds.Height));
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            // A press that lands on the floating viewport toolbar (the grip or a tool) drives that toolbar —
            // not the viewport camera/pick/marquee. Bail out so the toolbar's own drag/reorder gestures own it;
            // otherwise this bubbled press would arm a pick and a drag would start a box-select over the tools.
            if (e.Source is Visual source && source.FindAncestorOfType<ToolbarView>(includeSelf: true) is not null)
                return;

            // Click to focus so the keyboard drives this viewport; capture so look-drag keeps receiving
            // moves even past the control's bounds.
            _control.Focus();
            var point = e.GetCurrentPoint(_control);
            _buttons = ButtonsOf(point.Properties);
            _lastPointer = point.Position;
            _pointer = point.Position;
            _hasPointer = true;
            e.Pointer.Capture(_control);

            // Arm a pick for a plain left press (no camera-drag button held); a drag past the slop cancels it.
            _pendingPick = point.Properties.IsLeftButtonPressed
                && !point.Properties.IsRightButtonPressed
                && !point.Properties.IsMiddleButtonPressed;
            _pressPosition = point.Position;
            _pickAdditive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

            // Arm a right-tap context menu for a plain right press; a drag past the slop turns it into a pan.
            _rightTapCandidate = point.Properties.IsRightButtonPressed
                && !point.Properties.IsLeftButtonPressed
                && !point.Properties.IsMiddleButtonPressed;
            _rightPressPosition = point.Position;

            Send(0, 0, 0);
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            var point = e.GetCurrentPoint(_control);
            _buttons = ButtonsOf(point.Properties);
            if (_buttons == 0)
                e.Pointer.Capture(null);
            Send(0, 0, 0);

            // Finish a marquee, else treat a left press that never became a drag as a selection click. Both
            // hand control-space coordinates to the sink, which letterbox-maps them and asks the engine.
            // Editor views only — the game owns its input.
            if (_marqueeActive && e.InitialPressMouseButton == MouseButton.Left)
            {
                _marqueeActive = false;
                var bounds = _control.Bounds;
                var (x, y, width, height) = MarqueeRect(point.Position);
                Sink?.EndMarquee(x, y, width, height, bounds.Width, bounds.Height, _pickAdditive);
            }
            else if (_pendingPick && e.InitialPressMouseButton == MouseButton.Left
                     && Sink is { IsGame: false } sink)
            {
                var bounds = _control.Bounds;
                sink.Pick(point.Position.X, point.Position.Y, bounds.Width, bounds.Height, _pickAdditive);
            }
            else if (_rightTapCandidate && e.InitialPressMouseButton == MouseButton.Right
                     && Sink is { IsGame: false })
            {
                OpenContextMenu();
            }

            _pendingPick = false;
            _rightTapCandidate = false;
        }

        // Opens the data-driven context menu over the current selection (entity menu) or empty space
        // (background menu). The menu is a flyout in the popup layer — not a child of this input surface — so
        // the surface's pointer/focus capture doesn't light-dismiss it.
        private void OpenContextMenu()
        {
            if (ContextMenuService.Current is not { } service)
                return;

            var (menuId, context) = service.Selection.PrimaryId is { } id
                ? (MenuCatalogDefaults.EntityMenu,
                    new MenuContext { Host = MenuCatalogDefaults.EntityMenu, EntityId = id })
                : (MenuCatalogDefaults.BackgroundMenu,
                    new MenuContext { Host = MenuCatalogDefaults.BackgroundMenu, IsBackground = true });
            MenuOpenBehavior.Show(_control, menuId, context);
        }

        // The marquee rectangle (normalized to top-left + size, control space) from the press anchor to `current`.
        private (double X, double Y, double Width, double Height) MarqueeRect(Point current) =>
            (Math.Min(_pressPosition.X, current.X),
             Math.Min(_pressPosition.Y, current.Y),
             Math.Abs(current.X - _pressPosition.X),
             Math.Abs(current.Y - _pressPosition.Y));

        // Pushes the current marquee rectangle to the sink.
        private void UpdateMarqueeRect(Point current)
        {
            var (x, y, width, height) = MarqueeRect(current);
            Sink?.UpdateMarquee(x, y, width, height);
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

            // A press that moves past the slop is a drag, not a click: disarm the pick and, in an editor
            // viewport, begin a rubber-band marquee instead.
            if (_pendingPick)
            {
                var movedX = point.Position.X - _pressPosition.X;
                var movedY = point.Position.Y - _pressPosition.Y;
                if (movedX * movedX + movedY * movedY > ClickSlopSquared)
                {
                    _pendingPick = false;
                    // Box-select only with the select tool active; a transform tool owns the left-drag.
                    if (Sink is { IsGame: false, MarqueeEnabled: true })
                        _marqueeActive = true;
                }
            }

            // A right press that moves past the slop is a camera pan, not a tap — cancel the context menu.
            if (_rightTapCandidate)
            {
                var movedX = point.Position.X - _rightPressPosition.X;
                var movedY = point.Position.Y - _rightPressPosition.Y;
                if (movedX * movedX + movedY * movedY > ClickSlopSquared)
                    _rightTapCandidate = false;
            }

            if (_marqueeActive)
                UpdateMarqueeRect(point.Position);

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

            var inputKey = InputKeyMap.Map(e.Key);
            if (inputKey != InputKey.Unknown)
                changed |= _keys.Add(inputKey);

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

            var inputKey = InputKeyMap.Map(e.Key);
            if (inputKey != InputKey.Unknown)
                changed |= _keys.Remove(inputKey);

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
            _keys.Clear();
            _pendingPick = false;
            if (_marqueeActive)
            {
                _marqueeActive = false;
                Sink?.CancelMarquee();
            }

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
