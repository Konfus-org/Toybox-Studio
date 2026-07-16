using Avalonia.Data.Converters;
using Avalonia;
using System.Globalization;

namespace Toybox.Studio.Docking;

/// <summary>
/// A dockable (an <c>IDockable</c>) → its header icon's Lucide kind, read from the
/// <see cref="DockPanelRecord"/> the factory stamps onto each record. Null-safe: anything that isn't a
/// <see cref="DockPanelRecord"/> (e.g. a dockable restored from a layout saved before icons existed)
/// yields UnsetValue, so the bound <c>IconView</c> simply hides its glyph rather than logging a binding
/// error. The tab strip and chrome title-bar templates bind the whole dockable through
/// <see cref="Instance"/>.
/// </summary>
public sealed class DockPanelRecordIconConverter : IValueConverter
{
    /// <summary>Dockable → its Lucide header icon.</summary>
    public static readonly DockPanelRecordIconConverter Instance = new();

    private DockPanelRecordIconConverter()
    {
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DockPanelRecord record ? record.IconName : AvaloniaProperty.UnsetValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
