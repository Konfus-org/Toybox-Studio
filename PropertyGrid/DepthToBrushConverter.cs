using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Nesting depth (int) → a soft, warm overlay brush, so each level reads a faint notch deeper than its
/// parent without darkening into a heavy grey band. Depth 0 is transparent. The warm tint is the sole depth
/// cue now that the tree elbow is gone, stepping per level while capping before it collapses into a dark band.
/// </summary>
public sealed class DepthToBrushConverter : IValueConverter
{
    public static readonly DepthToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var depth = value is int level ? level : 0;
        if (depth <= 0)
            return Brushes.Transparent;

        var alpha = Math.Min(0.07 * depth, 0.28);
        return new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), 60, 44, 28));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
