using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// Turns a control's measured width into a <see cref="TranslateTransform"/> that shifts it left by half that
/// width, so a billboard placed by its top-left corner at the entity's projected point renders horizontally
/// centred on it. (Width isn't known until layout, so centring can't be done with a fixed offset.)
/// </summary>
public sealed class CenterXTransformConverter : IValueConverter
{
    public static readonly CenterXTransformConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new TranslateTransform(value is double width ? -width / 2.0 : 0.0, 0.0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
