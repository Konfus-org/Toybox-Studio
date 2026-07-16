using Avalonia.Data.Converters;
using System.Globalization;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Nesting depth (int) → true only at the top level (depth 0). A composite/list node renders as a full-width
/// accent section band at the top level and as an ordinary indented row when nested, so the grid distinguishes
/// the two by this.
/// </summary>
public sealed class DepthIsZeroConverter : IValueConverter
{
    public static readonly DepthIsZeroConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int depth && depth == 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
