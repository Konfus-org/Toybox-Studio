using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia;

namespace Toybox.Studio.Searching;

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

    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<SearchBox, bool>(nameof(IsBusy));

    public static readonly StyledProperty<double?> ProgressProperty =
        AvaloniaProperty.Register<SearchBox, double?>(nameof(Progress));

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

    /// <summary>Whether a query is in flight — shows the spinner to the right of the text.</summary>
    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    /// <summary>Fractional progress (0..1) of the active query, or <c>null</c> for an indeterminate
    /// spin. Forwarded to the spinner's determinate arc.</summary>
    public double? Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    private void OnClearClicked(object? sender, RoutedEventArgs e)
    {
        Text = "";
        this.FindControl<TextBox>("PART_TextBox")?.Focus();
    }
}
