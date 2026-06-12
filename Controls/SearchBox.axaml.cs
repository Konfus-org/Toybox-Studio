using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Toybox.Studio.Controls;

/// <summary>
/// A small, reusable search field: a magnifier icon, a text box, and a clear button that appears
/// once there is text to clear. Exposes <see cref="Text"/> and <see cref="Watermark"/> for binding,
/// so any widget can drop one in and filter against its <c>Text</c>.
/// </summary>
public partial class SearchBox : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<SearchBox, string>(
            nameof(Text), defaultValue: "", defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string> WatermarkProperty =
        AvaloniaProperty.Register<SearchBox, string>(nameof(Watermark), defaultValue: "Search…");

    public SearchBox()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    private void OnClearClicked(object? sender, RoutedEventArgs e)
    {
        Text = "";
        this.FindControl<TextBox>("PART_TextBox")?.Focus();
    }
}
