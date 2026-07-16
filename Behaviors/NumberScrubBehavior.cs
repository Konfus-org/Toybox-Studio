using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia;
using System.Linq;

namespace Toybox.Studio.Behaviors;

/// <summary>
/// Lets a <see cref="NumericUpDown"/> be scrubbed by dragging horizontally — drag right to raise the
/// value, left to lower it — so a number field is adjustable without typing. The drag arms only off the
/// field's frame ("the corners") and its up/down spinner buttons, never the text box, so click-to-caret
/// and typing behave normally; a genuine click on a spinner button still steps the value (the scrub only
/// takes over once the pointer travels past a small threshold, which cancels the button's press). While
/// scrubbing — and while merely hovering the frame — the cursor is the west-east resize arrows; the
/// spinner buttons keep the hand cursor that marks them clickable, and the text box keeps its I-beam.
/// Attach with <c>behaviors:NumberScrubBehavior.Enabled="True"</c>.
/// </summary>
public static class NumberScrubBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(NumberScrubBehavior));

    static NumberScrubBehavior() =>
        EnabledProperty.Changed.AddClassHandler<NumericUpDown>(OnEnabledChanged);

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    private static void OnEnabledChanged(NumericUpDown control, AvaloniaPropertyChangedEventArgs args)
    {
        // The scrubber wires itself to the control's events; the control's handler list keeps it alive
        // for exactly the control's lifetime, so there is nothing to detach.
        if (args.GetNewValue<bool>())
            _ = new Scrubber(control);
    }

    /// <summary>The per-field drag state and event wiring. One instance rides on each scrubbable field.</summary>
    private sealed class Scrubber
    {
        // How far the pointer must travel before a press becomes a scrub — below this a spinner-button
        // press is left alone to click and step normally.
        private const double DragThreshold = 3;

        // Pixels of horizontal travel per one Increment of value change.
        private const double PixelsPerStep = 8;

        private static readonly Cursor Resize = new(StandardCursorType.SizeWestEast);
        private static readonly Cursor Hand = new(StandardCursorType.Hand);
        private static readonly Cursor Beam = new(StandardCursorType.Ibeam);

        private readonly NumericUpDown _control;
        private readonly List<Button> _buttons = [];
        private TextBox? _textBox;

        private bool _pressed;
        private bool _dragging;
        private Point _origin;
        private decimal _startValue;

        public Scrubber(NumericUpDown control)
        {
            _control = control;

            // The frame/corner area shows the resize cursor by default; the template children override it
            // once resolved (text box → I-beam, spinner buttons → hand).
            control.Cursor = Resize;
            control.Loaded += OnLoaded;
            control.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            // The spinner buttons live in the ButtonSpinner's own template, so they only exist once the
            // whole field is loaded — resolve (and re-cursor) here rather than at TemplateApplied.
            _textBox = _control.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (_textBox is not null)
                _textBox.Cursor = Beam;

            _buttons.Clear();
            _buttons.AddRange(_control.GetVisualDescendants().OfType<Button>());
            foreach (var button in _buttons)
                button.Cursor = Hand;
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint(_control);
            // The text box is for typing — never scrub from it, so a click still places the caret.
            if (!point.Properties.IsLeftButtonPressed || IsWithin(e.Source as Visual, _textBox))
                return;

            _pressed = true;
            _dragging = false;
            _origin = point.Position;
            _startValue = _control.Value ?? _control.Minimum;
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_pressed)
                return;

            var dx = e.GetCurrentPoint(_control).Position.X - _origin.X;
            if (!_dragging)
            {
                if (Math.Abs(dx) < DragThreshold)
                    return;

                // Capturing here steals the pointer from a pressed spinner button, cancelling its click —
                // so a drag that began on a button scrubs instead of stepping.
                _dragging = true;
                e.Pointer.Capture(_control);
                SetButtonCursor(Resize);
            }

            var increment = _control.Increment == 0 ? 1m : _control.Increment;
            var steps = (decimal)Math.Round(dx / PixelsPerStep);
            _control.Value = Math.Clamp(_startValue + (steps * increment), _control.Minimum, _control.Maximum);
            e.Handled = true;
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_dragging)
            {
                e.Pointer.Capture(null);
                SetButtonCursor(Hand);
                // A drag is not a click — swallow the release so the spinner button doesn't also step.
                e.Handled = true;
            }

            _pressed = false;
            _dragging = false;
        }

        private void SetButtonCursor(Cursor cursor)
        {
            foreach (var button in _buttons)
                button.Cursor = cursor;
        }

        private static bool IsWithin(Visual? node, Visual? ancestor) =>
            ancestor is not null && node is not null
            && (ReferenceEquals(node, ancestor) || node.GetVisualAncestors().Contains(ancestor));
    }
}
