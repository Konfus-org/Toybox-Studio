using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.NodeGraph;

/// <summary>
/// A node's tail — a speech-bubble pointer (a filled triangle) from the node to the entity it represents. The
/// graph updates its geometry each frame from the node's rect and the entity's projected screen point; the
/// base sits on whichever node edge faces the entity (so a dragged-aside node still points back at it), tapering
/// to the apex at the entity point.
/// </summary>
public sealed partial class TailViewModel : ObservableObject
{
    [ObservableProperty]
    public partial Geometry? Geometry { get; private set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>Rebuilds the tail triangle for the node rect (<paramref name="left"/>, <paramref name="top"/>,
    /// <paramref name="width"/>, <paramref name="height"/>, in overlay px): the base of width
    /// <c>2·<paramref name="halfBase"/></c> sits on the edge the ray from the node's center to the entity point
    /// (<paramref name="apexX"/>, <paramref name="apexY"/>) exits, tapering to that apex.</summary>
    public void Update(
        double left, double top, double width, double height, double halfBase, double apexX, double apexY)
    {
        var centerX = left + width / 2;
        var centerY = top + height / 2;
        var dx = apexX - centerX;
        var dy = apexY - centerY;
        var halfWidth = width / 2;
        var halfHeight = height / 2;

        Point first;
        Point second;

        // Whichever edge the center→apex ray crosses first: the ray hits a vertical (left/right) edge when it
        // reaches the half-width sooner (in proportion) than the half-height, else a horizontal (top/bottom) edge.
        if (halfHeight <= 0 || Math.Abs(dx) * halfHeight >= Math.Abs(dy) * halfWidth)
        {
            var edgeX = dx >= 0 ? left + width : left;
            var reach = Math.Abs(dx) > 0.0001 ? halfWidth / Math.Abs(dx) : 0;
            var y = centerY + dy * reach;
            first = new Point(edgeX, y - halfBase);
            second = new Point(edgeX, y + halfBase);
        }
        else
        {
            var edgeY = dy >= 0 ? top + height : top;
            var reach = Math.Abs(dy) > 0.0001 ? halfHeight / Math.Abs(dy) : 0;
            var x = centerX + dx * reach;
            first = new Point(x - halfBase, edgeY);
            second = new Point(x + halfBase, edgeY);
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(first, isFilled: true);
            context.LineTo(new Point(apexX, apexY));
            context.LineTo(second);
            context.EndFigure(isClosed: true);
        }

        Geometry = geometry;
    }
}
