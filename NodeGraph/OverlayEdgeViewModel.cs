using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.NodeGraph;

/// <summary>
/// One reference link drawn in the overlay's edge layer (beneath the nodes): a wire from a source node's
/// <em>output</em> plug (its right edge) to a target node's <em>input</em> plug (its left edge). The graph
/// updates the endpoints each frame; the wire is a cubic Bézier whose control handles leave/enter horizontally,
/// so it bows out and around the nodes rather than cutting straight across them (and loops cleanly when the
/// target sits to the left of the source).
/// </summary>
public sealed partial class OverlayEdgeViewModel : ObservableObject
{
    // The minimum horizontal handle length (overlay px) — long enough that even coincident endpoints get a
    // visible, rounded wire rather than a kink.
    private const double MinHandle = 46;

    [ObservableProperty]
    public partial Geometry? Geometry { get; private set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>Rebuilds the wire from the source's output plug to the target's input plug: a cubic Bézier with
    /// horizontal tangents at both ends (out to the right of <paramref name="output"/>, in from the left of
    /// <paramref name="input"/>), so it routes around the node faces.</summary>
    public void Update(Point output, Point input)
    {
        var handle = Math.Max(MinHandle, Math.Abs(input.X - output.X) * 0.4);
        var control1 = new Point(output.X + handle, output.Y);
        var control2 = new Point(input.X - handle, input.Y);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(output, isFilled: false);
            context.CubicBezierTo(control1, control2, input);
            context.EndFigure(isClosed: false);
        }

        Geometry = geometry;
    }
}
