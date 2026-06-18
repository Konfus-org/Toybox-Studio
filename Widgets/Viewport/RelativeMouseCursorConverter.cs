using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Input;

namespace Toybox.Studio.Widgets.Viewport;

/// <summary>
/// Maps the viewport's relative-mouse flag and focus state to a cursor: hidden only while mouselook is
/// active <em>and</em> the viewport is focused (the panel re-centres the pointer itself), the normal arrow
/// otherwise. Gating on focus means Alt+Esc (which drops viewport focus) restores the cursor even though
/// the game still wants relative mode. Lets the view hide the cursor declaratively without the view-model
/// referencing any cursor type.
/// </summary>
public sealed class RelativeMouseCursorConverter : IMultiValueConverter
{
    public static readonly RelativeMouseCursorConverter Instance = new();

    private static readonly Cursor Hidden = new(StandardCursorType.None);

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [true, true] ? Hidden : Cursor.Default;
}
