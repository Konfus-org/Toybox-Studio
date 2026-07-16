using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// A vector axis label ("X" / "Y" / "Z") to its accent brush, so the fields read at a glance; W and
/// anything else fall back to a neutral foreground.
/// </summary>
public sealed class AxisLabelToBrushConverter : IValueConverter
{
    public static readonly AxisLabelToBrushConverter Instance = new();

    private static readonly IBrush X = new SolidColorBrush(Color.Parse("#E5784A"));
    private static readonly IBrush Y = new SolidColorBrush(Color.Parse("#8DBE52"));
    private static readonly IBrush Z = new SolidColorBrush(Color.Parse("#4F97E0"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string)?.ToUpperInvariant() switch
        {
            "X" => X,
            "Y" => Y,
            "Z" => Z,
            _ => Brushes.Gray,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
