using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Toybox.Studio.Overlays;

/// <summary>
/// A cursor-following hover card. It floats its <see cref="ContentControl.Content"/> next to the pointer like a
/// tooltip, but — unlike a real <see cref="ToolTip"/> — stays inside the visual tree rather than a separate
/// popup window, so GPU-interop content (a live 3D viewport) renders, and it is pointer-transparent so the
/// controls it floats over keep receiving hover/click.
///
/// Drop it in as the last child of a <see cref="Panel"/>, bind <see cref="IsOpen"/>, and set its content. It
/// tracks the pointer over that panel (or an explicit <see cref="TrackingElement"/>) and positions itself
/// beside the cursor, flipping sides to stay within the panel's bounds.
/// </summary>
public class CursorTooltip : ContentControl
{
    /// <summary>Whether the card is shown. When false it collapses and stops positioning.</summary>
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<CursorTooltip, bool>(nameof(IsOpen));

    /// <summary>The element whose pointer movement drives the card's position. Defaults to the visual parent
    /// (the host panel) when unset.</summary>
    public static readonly StyledProperty<InputElement?> TrackingElementProperty =
        AvaloniaProperty.Register<CursorTooltip, InputElement?>(nameof(TrackingElement));

    /// <summary>The gap, in DIPs, between the pointer and the card's near edge.</summary>
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<CursorTooltip, double>(nameof(Gap), 18d);

    // The element we currently listen to, and the latest pointer position (in the host panel's coordinates),
    // tracked even while closed so the card opens already placed under the cursor.
    private InputElement? _tracked;
    private Point _pointer;

    public CursorTooltip()
    {
        // The card never intercepts input: the tiles (or whatever) it floats over keep hovering/clicking, and
        // the host panel still sees the pointer-move events that drive positioning.
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        IsVisible = false;
    }

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public InputElement? TrackingElement
    {
        get => GetValue(TrackingElementProperty);
        set => SetValue(TrackingElementProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Unsubscribe();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TrackingElementProperty)
        {
            Unsubscribe();
            Subscribe();
        }
        else if (change.Property == IsOpenProperty)
        {
            IsVisible = IsOpen;
            Reposition();
        }
        else if (change.Property == BoundsProperty)
        {
            // The card's measured size is only known after layout; re-flip once we have it.
            Reposition();
        }
    }

    private void Subscribe()
    {
        _tracked = TrackingElement ?? this.GetVisualParent() as InputElement;

        // Listen on the host so every pointer move over its children (the tiles) repositions the card; tunnel +
        // bubble + handled-too makes this robust against children that mark the event handled.
        _tracked?.AddHandler(
            InputElement.PointerMovedEvent,
            OnPointerMoved,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void Unsubscribe()
    {
        _tracked?.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
        _tracked = null;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (this.GetVisualParent() is Visual parent)
            _pointer = e.GetPosition(parent);

        if (IsOpen)
            Reposition();
    }

    // Places the card beside the cursor, flipping to the opposite side and clamping so it stays within the host
    // panel's bounds.
    private void Reposition()
    {
        if (!IsOpen || this.GetVisualParent() is not Visual parent)
            return;

        var size = Bounds.Size.Width > 0 ? Bounds.Size : DesiredSize;
        var area = parent.Bounds.Size;

        var x = Place(_pointer.X, size.Width, area.Width);
        var y = Place(_pointer.Y, size.Height, area.Height);

        var margin = new Thickness(x, y, 0, 0);
        if (Margin != margin)
            Margin = margin;
    }

    // One axis: sit Gap past the cursor, but flip to before it when that would overflow, then clamp on-screen.
    private double Place(double cursor, double extent, double available)
    {
        var pos = cursor + Gap;
        if (pos + extent > available)
            pos = cursor - Gap - extent;

        return Math.Clamp(pos, 0, Math.Max(0, available - extent));
    }
}
