using Toybox.Studio.Utils;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Widgets.Console;

/// <summary>
/// A generic, content-agnostic console: a capped, searchable, copyable scrollback of
/// <see cref="ConsoleLine"/>s with batched UI updates. Feed it via <see cref="Append"/>; specific
/// consoles (e.g. the log console) own one of these and push lines into it.
/// </summary>
public sealed partial class ConsoleViewModel : ObservableObject
{
    private const int MaxLines = 1000;

    private readonly object _pendingLock = new();
    private readonly List<ConsoleLine> _pending = [];
    private bool _isFlushScheduled;

    /// <summary>
    /// Every line received, capped at <see cref="MaxLines"/>; the source of truth.
    /// </summary>
    public ObservableCollection<ConsoleLine> Lines { get; } = [];

    /// <summary>
    /// The subset of <see cref="Lines"/> matching the current search; what the view binds to.
    /// </summary>
    public ObservableCollection<ConsoleLine> VisibleLines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchSummary))]
    public partial string SearchText { get; set; } = "";

    /// <summary>
    /// A short "N matches" indicator, shown only while a search is active.
    /// </summary>
    public string MatchSummary =>
        string.IsNullOrEmpty(SearchText)
            ? ""
            : VisibleLines.Count == 1 ? "1 match" : $"{VisibleLines.Count} matches";

    /// <summary>
    /// True when nothing is shown (drives the empty-state ghost).
    /// </summary>
    public bool IsEmpty => VisibleLines.Count == 0;

    /// <summary>
    /// Appends a line. Safe to call from any thread; UI updates are batched.
    /// </summary>
    public void Append(ConsoleLine line)
    {
        lock (_pendingLock)
        {
            _pending.Add(line);
            if (_pending.Count > MaxLines)
                _pending.RemoveRange(0, _pending.Count - MaxLines);

            if (_isFlushScheduled)
                return;

            _isFlushScheduled = true;
        }

        Dispatch.To(DispatchContext.UI, Flush, DispatcherPriority.Background);
    }

    [RelayCommand]
    private void Clear()
    {
        Lines.Clear();
        VisibleLines.Clear();
        OnPropertyChanged(nameof(MatchSummary));
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnSearchTextChanged(string value) => RebuildVisible();

    private bool Matches(ConsoleLine line) =>
        string.IsNullOrEmpty(SearchText)
        || line.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

    private void RebuildVisible()
    {
        VisibleLines.Clear();
        foreach (var line in Lines)
        {
            if (Matches(line))
                VisibleLines.Add(line);
        }

        OnPropertyChanged(nameof(MatchSummary));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void Flush()
    {
        List<ConsoleLine> batch;
        lock (_pendingLock)
        {
            batch = [.. _pending];
            _pending.Clear();
            _isFlushScheduled = false;
        }

        if (batch.Count >= MaxLines)
        {
            Lines.Clear();
            VisibleLines.Clear();
            batch.RemoveRange(0, batch.Count - MaxLines);
        }

        foreach (var line in batch)
            AddLine(line);

        OnPropertyChanged(nameof(MatchSummary));
        OnPropertyChanged(nameof(IsEmpty));
    }

    // Appends one line, mirrors it into the filtered view if it matches, and trims the oldest line
    // from both collections once the cap is reached.
    private void AddLine(ConsoleLine line)
    {
        Lines.Add(line);
        if (Matches(line))
            VisibleLines.Add(line);

        while (Lines.Count > MaxLines)
        {
            var removed = Lines[0];
            Lines.RemoveAt(0);
            if (VisibleLines.Count > 0 && ReferenceEquals(VisibleLines[0], removed))
                VisibleLines.RemoveAt(0);
        }
    }
}
