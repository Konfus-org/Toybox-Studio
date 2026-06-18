using Toybox.Studio.Utils;
using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    // How close (in px) to the bottom still counts as "parked at the tail".
    private const double BottomThreshold = 2.0;

    private ScrollViewer? _scrollViewer;

    // While true, new output scrolls into view; the user scrolling up clears it, scrolling
    // back to the bottom restores it. Starts true so the console tails by default.
    private bool _stickToBottom = true;

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

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_scrollViewer is { })
            _scrollViewer.ScrollChanged -= OnScrollChanged;

        _scrollViewer = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");

        if (_scrollViewer is { })
            _scrollViewer.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollViewer is not { } scrollViewer)
            return;

        // Only react to the user moving the view. New output grows the extent (and our own
        // tailing scroll rides along with it); those come through as an extent change, which we
        // ignore so a growing log isn't mistaken for the user scrolling away from the bottom.
        if (e.ExtentDelta.Y != 0 || e.OffsetDelta.Y == 0)
            return;

        _stickToBottom =
            scrollViewer.Offset.Y >= scrollViewer.Extent.Height - scrollViewer.Viewport.Height - BottomThreshold;
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
        // Clearing the log (e.g. the Clear command) resets us to tailing.
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _stickToBottom = true;
            return;
        }

        // Follow the tail on new output, but only while the user is parked at the bottom and isn't
        // selecting lines — otherwise leave the view where they put it so they can read in peace.
        if (e.Action == NotifyCollectionChangedAction.Add && _stickToBottom && SelectedItems is { Count: 0 })
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
