using Avalonia;
using Avalonia.Data.Converters;
using System.Globalization;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Nesting depth (int) → a left indent (Thickness) applied inside the label gutter, so a nested row's label
/// tucks under its parent's expander while the shared value column stays aligned across every depth.
/// </summary>
public sealed class DepthToIndentConverter : IValueConverter
{
    public static readonly DepthToIndentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness((value is int depth ? depth : 0) * 14, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
