using Avalonia.Data.Converters;
using System.Globalization;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Nesting depth (int) → depth + 1. A child row binds its <c>Depth</c> to its parent row's through this, so
/// depth is threaded down the recursive <see cref="PropertyNodeView"/> tree by the view alone (no depth on the
/// node model).
/// </summary>
public sealed class DepthIncrementConverter : IValueConverter
{
    public static readonly DepthIncrementConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is int depth ? depth : 0) + 1;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
