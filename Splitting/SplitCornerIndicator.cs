using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;

namespace Toybox.Studio.Splitting;

/// <summary>
/// The transient overlay a <see cref="SplitCornerBehavior"/> draws on a pane's adorner layer: a small
/// triangle tucked into the hovered corner (the grab affordance), and — while a corner is being dragged
/// outward to merge — a tint over the pane that will close. Purely visual (never hit-testable) and
/// shared: only one gesture is ever in flight, so callers <see cref="ShowCorner"/> / <see cref="ShowClose"/>
/// as the pointer moves and <see cref="Clear"/> on release. Mirrors the toolbar's DockDragIndicator.
/// </summary>
public static class SplitCornerIndicator
{
    // The triangle's leg length in the corner.
    private const double CornerSize = 20;

    private static readonly IBrush FallbackAccent = new SolidColorBrush(Color.FromRgb(0x98, 0x88, 0xEB));

    private static Panel? _overlay;
    private static readonly Dictionary<SplitCorner, Path> Triangles = new();
    private static Border? _closeTint;

    /// <summary>Shows the grab triangle in <paramref name="corner"/> of <paramref name="host"/>.</summary>
    public static void ShowCorner(Control host, SplitCorner corner)
    {
        if (!Attach(host))
            return;

        _closeTint!.IsVisible = false;
        foreach (var (which, triangle) in Triangles)
            triangle.IsVisible = which == corner;
    }

    /// <summary>Tints <paramref name="host"/> to show its pane will close (merge into its sibling).</summary>
    public static void ShowClose(Control host)
    {
        if (!Attach(host))
            return;

        foreach (var triangle in Triangles.Values)
            triangle.IsVisible = false;
        _closeTint!.IsVisible = true;
    }

    /// <summary>Hides the overlay (hover ended or gesture released).</summary>
    public static void Clear() => Detach();

    private static bool Attach(Control host)
    {
        var layer = AdornerLayer.GetAdornerLayer(host);
        if (layer is null)
        {
            Clear();
            return false;
        }

        EnsureVisuals();
        if (!ReferenceEquals(_overlay!.Parent, layer))
        {
            Detach();
            layer.Children.Add(_overlay);
        }

        AdornerLayer.SetAdornedElement(_overlay, host);
        AdornerLayer.SetIsClipEnabled(_overlay, false);
        return true;
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

        var accent = Resource("ThemeAccentSolidBrush", FallbackAccent);
        var tint = Resource("ThemeMenuHighlightBrush", new SolidColorBrush(Color.FromArgb(0x33, 0x98, 0x88, 0xEB)));

        Triangles[SplitCorner.TopLeft] = Triangle(SplitCorner.TopLeft, accent);
        Triangles[SplitCorner.TopRight] = Triangle(SplitCorner.TopRight, accent);
        Triangles[SplitCorner.BottomLeft] = Triangle(SplitCorner.BottomLeft, accent);
        Triangles[SplitCorner.BottomRight] = Triangle(SplitCorner.BottomRight, accent);

        _closeTint = new Border
        {
            Background = tint,
            BorderBrush = accent,
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
            IsVisible = false,
        };

        _overlay = new Panel { IsHitTestVisible = false };
        _overlay.Children.Add(_closeTint);
        foreach (var triangle in Triangles.Values)
            _overlay.Children.Add(triangle);
    }

    private static Path Triangle(SplitCorner corner, IBrush fill) => new()
    {
        Width = CornerSize,
        Height = CornerSize,
        Fill = fill,
        IsHitTestVisible = false,
        IsVisible = false,
        HorizontalAlignment = corner.IsLeft() ? HorizontalAlignment.Left : HorizontalAlignment.Right,
        VerticalAlignment = corner.IsTop() ? VerticalAlignment.Top : VerticalAlignment.Bottom,
        Data = TriangleGeometry(corner),
    };

    // A right triangle whose square corner sits in the pane corner, legs running along the two edges.
    private static Geometry TriangleGeometry(SplitCorner corner)
    {
        const double s = CornerSize;
        var points = corner switch
        {
            SplitCorner.TopLeft => new[] { new Point(0, 0), new Point(s, 0), new Point(0, s) },
            SplitCorner.TopRight => new[] { new Point(s, 0), new Point(s, s), new Point(0, 0) },
            SplitCorner.BottomLeft => new[] { new Point(0, s), new Point(0, 0), new Point(s, s) },
            _ => new[] { new Point(s, s), new Point(0, s), new Point(s, 0) },
        };
        return new PolylineGeometry(points, true);
    }

    private static IBrush Resource(string key, IBrush fallback) =>
        Application.Current?.TryGetResource(key, null, out var value) == true && value is IBrush brush
            ? brush
            : fallback;
}
