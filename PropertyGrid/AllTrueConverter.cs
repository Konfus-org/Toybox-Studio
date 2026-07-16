using Avalonia.Data.Converters;
using System.Collections.Generic;
using System.Globalization;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Several bools → true only when every one is true (a logical AND over the bound inputs). Lets a control
/// require multiple independent conditions — e.g. a node renders as a top-level section band only when it is a
/// header <em>and</em> it sits at depth 0.
/// </summary>
public sealed class AllTrueConverter : IMultiValueConverter
{
    public static readonly AllTrueConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.All(value => value is true);
}
