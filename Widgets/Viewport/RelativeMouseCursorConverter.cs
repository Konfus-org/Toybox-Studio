using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Input;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// Maps the viewport's relative-mouse flag to a cursor: hidden while mouselook is active (the panel
/// re-centres the pointer itself), the normal arrow otherwise. Lets the view hide the cursor declaratively
/// without the view-model referencing any cursor type.
/// </summary>
public sealed class RelativeMouseCursorConverter : IValueConverter
{
    public static readonly RelativeMouseCursorConverter Instance = new();

    private static readonly Cursor Hidden = new(StandardCursorType.None);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Hidden : Cursor.Default;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
