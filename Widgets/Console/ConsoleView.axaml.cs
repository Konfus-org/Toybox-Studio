using Avalonia;
using Avalonia.Controls;

namespace Toybox.Studio.Widgets.Console;

/// <summary>
/// Generic console view: tails new lines, supports multi-select copy and select-all. The list behavior
/// lives in <see cref="ConsoleListBox"/>; this view only exposes the toolbar toggle.
/// </summary>
public partial class ConsoleView : UserControl
{
    /// <summary>
    /// Whether the search + clear toolbar is shown; hide it for a bare line list (e.g. splash).
    /// </summary>
    public static readonly StyledProperty<bool> ShowToolbarProperty =
        AvaloniaProperty.Register<ConsoleView, bool>(nameof(ShowToolbar), defaultValue: true);

    public ConsoleView()
    {
        InitializeComponent();
    }

    public bool ShowToolbar
    {
        get => GetValue(ShowToolbarProperty);
        set => SetValue(ShowToolbarProperty, value);
    }
}
