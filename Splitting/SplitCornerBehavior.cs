using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia;
using System.Runtime.CompilerServices;
using Toybox.Studio.Splitting;

namespace Toybox.Studio.Splitting;

/// <summary>
/// The Blender-style corner-split gesture, as a slap-on attached behavior: put
/// <c>splitting:SplitCornerBehavior.Enabled="True"</c> on any control that lives inside a
/// <see cref="SplitContainer"/> and it gains the whole interaction — hovering a corner shows a grab
/// triangle and a crosshair cursor; dragging <em>inward</em> from a corner splits the pane in two (the
/// panes resize live under the pointer, a tiny drag cancels); dragging a corner <em>outward</em> merges
/// the pane back into its sibling. The behavior finds its container as an ancestor and drives it — it
/// captures on that container, not the pane, so the live resize survives the container's rebuild.
/// </summary>
public static class SplitCornerBehavior
{
    // The square hot-zone in each corner, the move threshold before a drag counts, and the smallest a
    // split may leave a pane before a release is treated as "changed my mind" and cancelled.
    private const double CornerZone = 22;
    private const double DragThreshold = 5;
    private const double MinPaneSize = 60;

    private static readonly Cursor CrossCursor = new(StandardCursorType.Cross);
    private static readonly ConditionalWeakTable<Control, HoverState> Hovers = new();

    // The single in-flight gesture (one pointer at a time); its move/release run on the container.
    private static Drag? _drag;

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(SplitCornerBehavior));

    static SplitCornerBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<Control>(OnEnabledChanged);
    }

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    private static void OnEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.GetNewValue<bool>())
        {
            control.AddHandler(InputElement.PointerMovedEvent, OnHostMoved, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerPressedEvent, OnHostPressed, RoutingStrategies.Tunnel);
            control.AddHandler(InputElement.PointerExitedEvent, OnHostExited, RoutingStrategies.Tunnel);
        }
        else
        {
            control.RemoveHandler(InputElement.PointerMovedEvent, OnHostMoved);
            control.RemoveHandler(InputElement.PointerPressedEvent, OnHostPressed);
            control.RemoveHandler(InputElement.PointerExitedEvent, OnHostExited);
        }
    }

    // --- Hover affordance (runs on the pane, only while no drag is active) ---

    private static void OnHostMoved(object? sender, PointerEventArgs args)
    {
        if (_drag is not null || sender is not Control host)
            return;

        var corner = CornerAt(args.GetPosition(host), host.Bounds.Size);
        var state = Hovers.GetValue(host, _ => new HoverState());
        if (state.Corner == corner)
            return;

        state.Corner = corner;
        if (corner is { } value)
        {
            SplitCornerIndicator.ShowCorner(host, value);
            host.Cursor = CrossCursor;
        }
        else
        {
            SplitCornerIndicator.Clear();
            host.Cursor = null;
        }
    }

    private static void OnHostExited(object? sender, PointerEventArgs args)
    {
        if (_drag is not null || sender is not Control host)
            return;

        Hovers.GetValue(host, _ => new HoverState()).Corner = null;
        SplitCornerIndicator.Clear();
        host.Cursor = null;
    }

    private static void OnHostPressed(object? sender, PointerPressedEventArgs args)
    {
        if (_drag is not null || sender is not Control host)
            return;
        if (!args.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
            return;

        var corner = CornerAt(args.GetPosition(host), host.Bounds.Size);
        if (corner is not { } value
            || host.FindAncestorOfType<SplitContainer>() is not { } container
            || host.TranslatePoint(default, container) is not { } origin)
            return;

        _drag = new Drag
        {
            Host = host,
            Container = container,
            Corner = value,
            Start = args.GetPosition(container),
            Region = new Rect(origin, host.Bounds.Size),
        };

        container.AddHandler(InputElement.PointerMovedEvent, OnDragMoved, RoutingStrategies.Tunnel);
        container.AddHandler(InputElement.PointerReleasedEvent, OnDragReleased, RoutingStrategies.Tunnel);
        container.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        args.Pointer.Capture(container);
        host.Cursor = CrossCursor;
        args.Handled = true;
    }

    // --- Drag (runs on the container, which is stable across its own rebuilds) ---

    private static void OnDragMoved(object? sender, PointerEventArgs args)
    {
        if (_drag is not { } drag)
            return;

        var pointer = args.GetPosition(drag.Container);
        switch (drag.Mode)
        {
            case DragMode.Pending:
                Decide(drag, pointer);
                break;
            case DragMode.Split:
                drag.LastRatio = RatioAt(drag, pointer);
                drag.Container.UpdateSplit(drag.LastRatio);
                break;
        }

        args.Handled = true;
    }

    // First real movement picks the gesture: inward from the corner splits along the dominant axis;
    // outward merges the pane into its sibling.
    private static void Decide(Drag drag, Point pointer)
    {
        var dx = pointer.X - drag.Start.X;
        var dy = pointer.Y - drag.Start.Y;
        if (Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
            return;

        // Split vs. merge is the overall pull relative to the corner's inward diagonal (toward the pane's
        // centre → split; away, out through the corner → merge), so a mostly-outward diagonal merges even
        // when its dominant axis happens to point inward. The split's orientation is the dominant axis.
        var alongX = Math.Abs(dx) >= Math.Abs(dy);
        var inward = dx * drag.Corner.InwardX() + dy * drag.Corner.InwardY();

        if (inward > 0)
        {
            drag.Orientation = alongX ? SplitOrientation.Horizontal : SplitOrientation.Vertical;
            var ratio = RatioAt(drag, pointer);
            var newFirst = drag.Orientation == SplitOrientation.Horizontal ? drag.Corner.IsLeft() : drag.Corner.IsTop();
            drag.Mode = drag.Container.BeginSplit(drag.Host, drag.Orientation, ratio, newFirst)
                ? DragMode.Split
                : DragMode.Dead;
            drag.LastRatio = ratio;
            SplitCornerIndicator.Clear();
        }
        else if (drag.Container.CanClose(drag.Host))
        {
            drag.Mode = DragMode.Close;
            SplitCornerIndicator.ShowClose(drag.Host);
        }
        else
        {
            drag.Mode = DragMode.Dead;
        }
    }

    private static void OnDragReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_drag is not { } drag)
            return;

        if (drag.Mode == DragMode.Split)
        {
            var extent = drag.Orientation == SplitOrientation.Horizontal ? drag.Region.Width : drag.Region.Height;
            var smaller = extent * Math.Min(drag.LastRatio, 1 - drag.LastRatio);
            if (smaller < MinPaneSize)
                drag.Container.CancelSplit();
            else
                drag.Container.CommitSplit();
        }
        else if (drag.Mode == DragMode.Close
                 && !drag.Region.Contains(args.GetPosition(drag.Container))
                 && drag.Container.CanClose(drag.Host))
        {
            drag.Container.ClosePane(drag.Host);
        }

        EndDrag();
        args.Handled = true;
    }

    private static void OnCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        if (_drag is { Mode: DragMode.Split } drag)
            drag.Container.CommitSplit();
        EndDrag();
    }

    private static void EndDrag()
    {
        if (_drag is not { } drag)
            return;

        drag.Container.RemoveHandler(InputElement.PointerMovedEvent, OnDragMoved);
        drag.Container.RemoveHandler(InputElement.PointerReleasedEvent, OnDragReleased);
        drag.Container.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        drag.Host.Cursor = null;
        SplitCornerIndicator.Clear();
        _drag = null;
    }

    // The divider position the pointer maps to, as the first child's fraction of the split region.
    private static double RatioAt(Drag drag, Point pointer)
    {
        var (start, extent) = drag.Orientation == SplitOrientation.Horizontal
            ? (drag.Region.X, drag.Region.Width)
            : (drag.Region.Y, drag.Region.Height);
        if (extent <= 0)
            return 0.5;

        var along = (drag.Orientation == SplitOrientation.Horizontal ? pointer.X : pointer.Y) - start;
        return Math.Clamp(along / extent, SplitBranch.MinRatio, 1 - SplitBranch.MinRatio);
    }

    private static SplitCorner? CornerAt(Point pointer, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0
            || pointer.X < 0 || pointer.Y < 0 || pointer.X > size.Width || pointer.Y > size.Height)
            return null;

        var left = pointer.X <= CornerZone;
        var right = pointer.X >= size.Width - CornerZone;
        var top = pointer.Y <= CornerZone;
        var bottom = pointer.Y >= size.Height - CornerZone;

        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => SplitCorner.TopLeft,
            (_, true, true, _) => SplitCorner.TopRight,
            (true, _, _, true) => SplitCorner.BottomLeft,
            (_, true, _, true) => SplitCorner.BottomRight,
            _ => null,
        };
    }

    private sealed class HoverState
    {
        public SplitCorner? Corner;
    }

    private enum DragMode
    {
        Pending,
        Split,
        Close,
        Dead,
    }

    private sealed class Drag
    {
        public required Control Host { get; init; }
        public required SplitContainer Container { get; init; }
        public required SplitCorner Corner { get; init; }
        public required Point Start { get; init; }
        public required Rect Region { get; init; }
        public DragMode Mode { get; set; } = DragMode.Pending;
        public SplitOrientation Orientation { get; set; }
        public double LastRatio { get; set; } = 0.5;
    }
}
