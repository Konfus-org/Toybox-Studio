using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;

namespace Toybox.Studio.Widgets.Colors;

/// <summary>
/// A small, reusable colour field: the Fluent <c>ColorPicker</c> dropdown paired with a monospace
/// hex readout. Exposes a two-way <see cref="Color"/> so any widget can bind a colour and let the
/// user edit it via the spectrum, sliders, or hex entry in the flyout.
/// </summary>
public partial class ColorPickerField : UserControl
{
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<ColorPickerField, Color>(
            nameof(Color), defaultValue: Avalonia.Media.Colors.White, defaultBindingMode: BindingMode.TwoWay);

    public ColorPickerField()
    {
        InitializeComponent();
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }
}
