using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Toybox.Studio.NodeGraph;

/// <summary>Colors a node's reference plug by what it points at: an entity (primary), an asset (info), or a
/// script (success), matching the studio palette.</summary>
public sealed class ReferenceKindBrushConverter : IValueConverter
{
    public static readonly ReferenceKindBrushConverter Instance = new();

    private static readonly IBrush Entity = new SolidColorBrush(Color.Parse("#A99BF0"));
    private static readonly IBrush Asset = new SolidColorBrush(Color.Parse("#86C5E8"));
    private static readonly IBrush Script = new SolidColorBrush(Color.Parse("#8FD9AE"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ReferenceKind.Entity => Entity,
            ReferenceKind.Script => Script,
            _ => Asset,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
