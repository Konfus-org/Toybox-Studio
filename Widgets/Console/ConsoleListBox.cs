using Toybox.Studio.Utils;
using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Widgets.Console;

/// <summary>
/// The console's line list as a self-contained control: tails new output, and offers multi-select copy
/// and select-all as commands (also Ctrl+C). This keeps the clipboard/scroll concerns — which are inherently
/// view-level — out of the console's code-behind, so the view binds purely declaratively.
/// </summary>
public class ConsoleListBox : ListBox
{
    public ConsoleListBox()
    {
        CopySelectionCommand = new RelayCommand(CopySelection);
        SelectAllCommand = new RelayCommand(SelectAll);
    }

    // A ListBox subclass keys its ControlTheme off its own type by default, so it would resolve no theme
    // and render untemplated (no ItemsPresenter → no visible rows, even with items bound). Point the style
    // key back at ListBox so it picks up the Fluent ListBox template.
    protected override Type StyleKeyOverride => typeof(ListBox);

    /// <summary>Copies the selected lines (in visual order) to the clipboard.</summary>
    public System.Windows.Input.ICommand CopySelectionCommand { get; }

    /// <summary>Selects every visible line.</summary>
    public System.Windows.Input.ICommand SelectAllCommand { get; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // VisibleLines is a stable collection (cleared, never reassigned), so one subscription suffices.
        if (ItemsView is INotifyCollectionChanged lines)
        {
            lines.CollectionChanged -= OnLinesChanged;
            lines.CollectionChanged += OnLinesChanged;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            CopySelection();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Follow the tail on new output, but don't yank the view while the user is selecting lines.
        if (e.Action == NotifyCollectionChangedAction.Add && SelectedItems is { Count: 0 })
            ScrollIntoView(ItemCount - 1);
    }

    private void CopySelection()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;
        if (SelectedItems is not { Count: > 0 } selectedItems)
            return;

        var selected = selectedItems.Cast<object>().ToHashSet();
        // Copy in visual order (selection order is arbitrary), one line per row.
        var text = string.Join(
            "\n",
            Items.OfType<ConsoleLine>().Where(selected.Contains).Select(line => line.Text));

        if (!string.IsNullOrEmpty(text))
            clipboard.SetTextAsync(text).FireAndForget();
    }
}
