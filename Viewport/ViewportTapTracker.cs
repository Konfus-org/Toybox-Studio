using Avalonia;
using Avalonia.Input;
using Toybox.Studio.Input;

namespace Toybox.Studio.Viewport;

/// <summary>
/// Distills the viewport's forwarded input snapshots into tap gestures: a lone left press arms a
/// pick, drifting past the click slop or growing a second button (a camera gesture) disarms it, and
/// the release of a still-armed press is the tap — so a fly-camera drag, a gizmo drag, and a marquee
/// can never read as a click. Pure per-viewport state; the view-model feeds it every snapshot it
/// forwards and picks on the taps that come back.
/// </summary>
public sealed class ViewportTapTracker
{
    // A press may wobble this far (squared logical pixels) and still be a click.
    private const double SlopSquared = 16.0;

    private bool _armed;
    private bool _leftWasDown;
    private Point _pressPosition;

    /// <summary>Feeds one forwarded snapshot; the completed tap when this one finished a click, else
    /// null.</summary>
    public ViewportTap? Track(InputSnapshot input)
    {
        var leftDown = input.Buttons.Contains(MouseButton.Left);
        var otherDown = input.Buttons.Any(button => button != MouseButton.Left);
        ViewportTap? tap = null;

        if (leftDown && !_leftWasDown)
        {
            // Arm only a lone left press: left joining a camera gesture is never a pick.
            _armed = !otherDown;
            _pressPosition = input.PointerPosition;
        }
        else if (_armed && leftDown)
        {
            var dx = input.PointerPosition.X - _pressPosition.X;
            var dy = input.PointerPosition.Y - _pressPosition.Y;
            if (otherDown || (dx * dx) + (dy * dy) > SlopSquared)
                _armed = false;
        }
        else if (_armed && !leftDown && _leftWasDown)
        {
            _armed = false;
            // A focus loss also drops the buttons; only a real, still-focused release is a tap.
            if (input.Focused && !otherDown)
            {
                tap = new ViewportTap(
                    input.NormalizedPointer.X,
                    input.NormalizedPointer.Y,
                    Toggle: input.Keys.Contains(Key.LeftCtrl) || input.Keys.Contains(Key.RightCtrl),
                    Additive: input.Keys.Contains(Key.LeftShift) || input.Keys.Contains(Key.RightShift));
            }
        }

        _leftWasDown = leftDown;
        return tap;
    }
}
