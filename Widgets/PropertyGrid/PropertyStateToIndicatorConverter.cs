using System;
using System.Globalization;
using Avalonia.Controls.Shapes;
using Avalonia.Data.Converters;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Maps a <see cref="PropertyState"/> to the control that represents it in a row's right-hand indicator
/// slot. Each state returns a fresh control (one per row), themed via the <c>stateFilled</c>/<c>stateHollow</c>
/// styles in <c>PropertyGridView.axaml</c> so colors track the active theme. Extending the indicator set is
/// one new enum value plus one case here.
/// </summary>
public sealed class PropertyStateToIndicatorConverter : IValueConverter
{
    public static readonly PropertyStateToIndicatorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            PropertyState.ReadOnly => new IconView { IconName = "Lock", IconColor = "YELLOW", Width = 13, Height = 13 },
            PropertyState.NonDefault => new Ellipse { Classes = { "stateFilled" } },
            PropertyState.Default => new Ellipse { Classes = { "stateHollow" } },
            _ => null,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
