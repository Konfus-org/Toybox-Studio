using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Toybox.Studio.Searching;

/// <summary>
/// Turns a 0..1 progress value into a ring-arc geometry for the <see cref="Spinner"/>'s determinate
/// mode: an arc starting at the top (12 o'clock) and sweeping clockwise by <c>value * 360°</c>. Zero
/// yields nothing; a full (or over-full) value yields the closed ring. Coordinates are in the spinner's
/// 14×14 space (centre 7,7, radius 6). A null/out-of-range value yields <c>null</c> (nothing drawn).
/// </summary>
public sealed class ProgressArcConverter : IValueConverter
{
    private const double Center = 7;
    private const double Radius = 6;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double progress)
            return null;

        progress = Math.Clamp(progress, 0, 1);
        if (progress <= 0)
            return null;

        if (progress >= 1)
            return new EllipseGeometry(new Rect(Center - Radius, Center - Radius, Radius * 2, Radius * 2));

        var sweep = progress * 360.0;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(PointOnCircle(-90), isFilled: false);
            context.ArcTo(
                PointOnCircle(-90 + sweep), new Size(Radius, Radius), 0,
                isLargeArc: sweep > 180, SweepDirection.Clockwise);
            context.EndFigure(isClosed: false);
        }

        return geometry;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Point PointOnCircle(double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point(Center + Radius * Math.Cos(radians), Center + Radius * Math.Sin(radians));
    }
}
