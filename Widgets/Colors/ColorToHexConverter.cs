using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Toybox.Studio.Widgets.Colors;

/// <summary>Formats an Avalonia <see cref="Color"/> as <c>#RRGGBBAA</c> for hex readouts.</summary>
public sealed class ColorToHexConverter : IValueConverter
{
    public static readonly ColorToHexConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Color c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}" : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
