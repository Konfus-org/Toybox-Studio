using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia;
using Toybox.Studio.Toolbar;

namespace Toybox.Studio.Toolbar;

/// <summary>
/// A transient overlay drawn on the viewport's adorner layer while the toolbar grip is dragged: a small
/// "drop zone" pill hugging each viewport edge and corner the toolbar can dock to, with the placement
/// nearest the pointer lit up. Purely visual (never hit-testable) and shared — only one drag is ever in
/// flight, so callers just <see cref="Show"/> the host + active placement as the pointer moves and
/// <see cref="Clear"/> on release.
/// </summary>
public static class DockDragIndicator
{
    private static Panel? _overlay;
    private static readonly Dictionary<ToolbarEdge, Border> Markers = new();

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

    // The active placement reads as a filled accent pill; the rest are dim outlined hints of where
    // it could go.
    private static void Highlight(ToolbarEdge active)
    {
        var accentFill = Resource("ThemeMenuHighlightBrush", FallbackAccentFill);
        var accentEdge = Resource("ThemeAccentSolidBrush", FallbackAccentEdge);
        var idleFill = Resource("ThemeScrimBrush", FallbackIdleFill);
        var idleEdge = Resource("ThemeBorderBrush", FallbackIdleEdge);

        foreach (var (placement, marker) in Markers)
            Apply(marker, placement == active, accentFill, accentEdge, idleFill, idleEdge);
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

        Markers[ToolbarEdge.Top] = Marker(HorizontalAlignment.Center, VerticalAlignment.Top, horizontal: true);
        Markers[ToolbarEdge.Bottom] = Marker(HorizontalAlignment.Center, VerticalAlignment.Bottom, horizontal: true);
        Markers[ToolbarEdge.Left] = Marker(HorizontalAlignment.Left, VerticalAlignment.Center, horizontal: false);
        Markers[ToolbarEdge.Right] = Marker(HorizontalAlignment.Right, VerticalAlignment.Center, horizontal: false);
        Markers[ToolbarEdge.TopLeft] = Marker(HorizontalAlignment.Left, VerticalAlignment.Top, horizontal: true);
        Markers[ToolbarEdge.TopRight] = Marker(HorizontalAlignment.Right, VerticalAlignment.Top, horizontal: true);
        Markers[ToolbarEdge.BottomLeft] = Marker(HorizontalAlignment.Left, VerticalAlignment.Bottom, horizontal: true);
        Markers[ToolbarEdge.BottomRight] = Marker(HorizontalAlignment.Right, VerticalAlignment.Bottom, horizontal: true);

        _overlay = new Panel { IsHitTestVisible = false };
        foreach (var marker in Markers.Values)
            _overlay.Children.Add(marker);
    }

    // A rounded pill echoing the docked toolbar's footprint: wide+short on the top/bottom edges and in the
    // corners (where the toolbar lies horizontally), narrow+tall on the left/right edges, inset from the
    // edge by the same margin the real toolbar uses.
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
