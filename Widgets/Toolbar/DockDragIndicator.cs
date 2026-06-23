using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Toybox.Studio.Widgets.Toolbar;

/// <summary>
/// A transient overlay drawn on the viewport's adorner layer while the toolbar grip is dragged: a small
/// "drop zone" pill hugging each viewport edge the toolbar can dock to (top/bottom/left/right, centred along
/// the edge), with the edge nearest the pointer lit up. Purely visual (never hit-testable) and shared — only
/// one drag is ever in flight, so callers just <see cref="Show"/> the host + active edge as the pointer moves
/// and <see cref="Clear"/> on release.
/// </summary>
public static class DockDragIndicator
{
    private static Panel? _overlay;
    private static Border? _top;
    private static Border? _bottom;
    private static Border? _left;
    private static Border? _right;

    private static readonly IBrush FallbackAccentFill = new SolidColorBrush(Color.FromArgb(0x3A, 0x98, 0x88, 0xEB));
    private static readonly IBrush FallbackAccentEdge = new SolidColorBrush(Color.FromRgb(0x98, 0x88, 0xEB));
    private static readonly IBrush FallbackIdleFill = new SolidColorBrush(Color.FromArgb(0x55, 0xEF, 0xE6, 0xD4));
    private static readonly IBrush FallbackIdleEdge = new SolidColorBrush(Color.FromArgb(0x40, 0x4A, 0x40, 0x36));

    /// <summary>Shows the dock markers over <paramref name="host"/>, lighting up <paramref name="active"/>.</summary>
    public static void Show(Visual host, ToolbarEdge active)
    {
        var layer = AdornerLayer.GetAdornerLayer(host);
        if (layer is null)
        {
            Clear();
            return;
        }

        EnsureVisuals();
        if (!ReferenceEquals(_overlay!.Parent, layer))
        {
            Detach();
            layer.Children.Add(_overlay);
        }

        AdornerLayer.SetAdornedElement(_overlay, host);
        AdornerLayer.SetIsClipEnabled(_overlay, false);
        Highlight(active);
    }

    /// <summary>Hides the markers (drag ended).</summary>
    public static void Clear() => Detach();

    // The active edge reads as a filled accent pill; the rest are dim outlined hints of where it could go.
    private static void Highlight(ToolbarEdge active)
    {
        var accentFill = Resource("ThemeMenuHighlightBrush", FallbackAccentFill);
        var accentEdge = Resource("ThemeAccentSolidBrush", FallbackAccentEdge);
        var idleFill = Resource("ThemeScrimBrush", FallbackIdleFill);
        var idleEdge = Resource("ThemeBorderBrush", FallbackIdleEdge);

        Apply(_top!, active == ToolbarEdge.Top, accentFill, accentEdge, idleFill, idleEdge);
        Apply(_bottom!, active == ToolbarEdge.Bottom, accentFill, accentEdge, idleFill, idleEdge);
        Apply(_left!, active == ToolbarEdge.Left, accentFill, accentEdge, idleFill, idleEdge);
        Apply(_right!, active == ToolbarEdge.Right, accentFill, accentEdge, idleFill, idleEdge);
    }

    private static void Apply(
        Border marker, bool on, IBrush accentFill, IBrush accentEdge, IBrush idleFill, IBrush idleEdge)
    {
        marker.Background = on ? accentFill : idleFill;
        marker.BorderBrush = on ? accentEdge : idleEdge;
        marker.Opacity = on ? 1.0 : 0.5;
    }

    private static void Detach()
    {
        if (_overlay?.Parent is AdornerLayer layer)
            layer.Children.Remove(_overlay);
    }

    private static void EnsureVisuals()
    {
        if (_overlay is not null)
            return;

        _top = Marker(HorizontalAlignment.Center, VerticalAlignment.Top, horizontal: true);
        _bottom = Marker(HorizontalAlignment.Center, VerticalAlignment.Bottom, horizontal: true);
        _left = Marker(HorizontalAlignment.Left, VerticalAlignment.Center, horizontal: false);
        _right = Marker(HorizontalAlignment.Right, VerticalAlignment.Center, horizontal: false);
        _overlay = new Panel { IsHitTestVisible = false, Children = { _top, _bottom, _left, _right } };
    }

    // A rounded pill echoing the docked toolbar's footprint: wide+short on the top/bottom edges, narrow+tall
    // on the left/right edges, inset from the edge by the same margin the real toolbar uses.
    private static Border Marker(HorizontalAlignment h, VerticalAlignment v, bool horizontal) => new()
    {
        Width = horizontal ? 46 : 18,
        Height = horizontal ? 18 : 46,
        Margin = new Thickness(8),
        CornerRadius = new CornerRadius(6),
        BorderThickness = new Thickness(1.5),
        HorizontalAlignment = h,
        VerticalAlignment = v,
        IsHitTestVisible = false,
    };

    private static IBrush Resource(string key, IBrush fallback) =>
        Application.Current?.TryGetResource(key, null, out var value) == true && value is IBrush brush
            ? brush
            : fallback;
}
