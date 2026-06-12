using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Toybox.Studio.Widgets.Console;

/// <summary>
/// Generic console view: tails new lines, supports multi-select copy and select-all.
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
        DataContextChanged += (_, _) =>
        {
            if (DataContext is ConsoleViewModel viewModel)
                viewModel.VisibleLines.CollectionChanged += OnLinesChanged;
        };
    }

    public bool ShowToolbar
    {
        get => GetValue(ShowToolbarProperty);
        set => SetValue(ShowToolbarProperty, value);
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Follow the tail on new output, but don't yank the view while the user is selecting lines.
        if (e.Action == NotifyCollectionChangedAction.Add && LineList.SelectedItems is { Count: 0 })
            LineList.ScrollIntoView(LineList.ItemCount - 1);
    }

    private void OnLineKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            CopySelection();
            e.Handled = true;
        }
    }

    private void OnCopyClicked(object? sender, RoutedEventArgs e) => CopySelection();

    private void OnSelectAllClicked(object? sender, RoutedEventArgs e) => LineList.SelectAll();

    private void CopySelection()
    {
        if (DataContext is not ConsoleViewModel viewModel)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
            return;

        var selected = LineList.SelectedItems?.Cast<ConsoleLine>().ToHashSet();
        if (selected is not { Count: > 0 })
            return;

        // Copy in visual order (selection order is arbitrary), one line per row.
        var text = string.Join(
            "\n",
            viewModel.VisibleLines.Where(selected.Contains).Select(line => line.Text));

        if (!string.IsNullOrEmpty(text))
            clipboard.SetTextAsync(text).FireAndForget();
    }
}
