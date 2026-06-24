using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Several bools → true when any is true (a logical OR over the bound inputs). Lets a row stay visible when
/// either of two independent conditions holds — e.g. a script card shown when its title matches the search
/// <em>or</em> its override grid still has a matching row.
/// </summary>
public sealed class AnyTrueConverter : IMultiValueConverter
{
    public static readonly AnyTrueConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Any(value => value is true);
}

/// <summary>
/// Several bools → true only when every one is true (a logical AND over the bound inputs). Lets a control
/// require multiple independent conditions — e.g. the empty-state ghost shown only when the grid is empty
/// <em>and</em> it isn't an embedded grid (whose host card already supplies the context).
/// </summary>
public sealed class AllTrueConverter : IMultiValueConverter
{
    public static readonly AllTrueConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.All(value => value is true);
}

/// <summary>
/// Nesting depth (int) → a soft, warm overlay brush, so each level reads a faint notch deeper than its
/// parent without darkening the clay into a heavy grey band. Depth 0 is transparent. Mirrors the
/// inspector's per-level shading.
/// </summary>
public sealed class DepthToBrushConverter : IValueConverter
{
    public static readonly DepthToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var depth = value is int d ? d : 0;
        if (depth <= 0)
            return Brushes.Transparent;

        // A warm deepening (warm-brown tone) per level — the sole depth cue now that the elbow connector is
        // gone, so it steps clearly per level while still capping before it collapses into a heavy dark band.
        var alpha = Math.Min(0.07 * depth, 0.28);
        return new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), 60, 44, 28));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Nesting depth (int) → a left indent (Thickness), so a nested row's label tucks under its parent's
/// expander while the value column stays fixed.
/// </summary>
public sealed class DepthToIndentConverter : IValueConverter
{
    public static readonly DepthToIndentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var depth = value is int d ? d : 0;
        return new Thickness(depth * 14, 0, 0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Vector axis label ("X"/"Y"/"Z"/"W") → its accent brush, matching the editor's axis colours. Anything
/// else (or W) falls back to a neutral foreground.
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
