using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Maps a <see cref="PropertyState"/> to the control that represents it in a row's right-hand indicator
/// slot. Each state returns a fresh control (one per row), themed via the <c>stateFilled</c>/<c>stateHollow</c>
/// styles in <c>PropertyGridView.axaml</c> so colors track the active theme. The dots are Borders (not
/// Ellipses) so they can carry a clay BoxShadow — a raised pill when the value is set, an inset crater when
/// it is at its default. Extending the indicator set is one new enum value plus one case here.
/// </summary>
public sealed class PropertyStateToIndicatorConverter : IValueConverter
{
    public static readonly PropertyStateToIndicatorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            PropertyState.ReadOnly => new IconView { IconName = "Lock", IconColor = "YELLOW", Width = 13, Height = 13 },
            PropertyState.NonDefault => new Border { Classes = { "stateFilled" } },
            PropertyState.Default => new Border { Classes = { "stateHollow" } },
            _ => null,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
