using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Toybox.Studio.Clipboards;
using Toybox.Studio.Utils.Extensions;
using Toybox.Studio.Utils.Search;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Console;

/// <summary>
/// A generic, content-agnostic console: a capped, searchable, copyable scrollback of
/// <see cref="ConsoleLine"/>s with batched UI updates, a text search, and per-severity show/hide filters.
/// Feed it via <see cref="Append"/>; specific consoles (e.g. the log console) own one of these and push
/// lines into it.
/// </summary>
public sealed partial class ConsoleViewModel : ObservableObject
{
    private const int MaxLines = 1000;

    private readonly Clipboard _clipboard;

    private readonly object _pendingLock = new();
    private readonly List<ConsoleLine> _pending = [];
    private bool _isFlushScheduled;

    private int _infoCount;
    private int _warningCount;
    private int _errorCount;

    public ConsoleViewModel(Clipboard clipboard)
    {
        _clipboard = clipboard;

        // The full rebuild (on a text-search or severity change) runs off the UI thread: the snapshot
        // applies the severity filters here, the strategy substring-matches the text, and the results
        // reconcile into VisibleLines. Incremental appends (Append/AddLine) stay synchronous.
        Search = new SearchController<ConsoleLine>(
            new SubstringSearchStrategy<ConsoleLine>(line => line.Text),
            () => Lines.Where(line => SeverityShown(line.Severity)).ToList(),
            ApplyResults);
    }

    /// <summary>Drives the search box: its <c>Query</c> is the search text, <c>IsBusy</c> the spinner.</summary>
    public SearchController<ConsoleLine> Search { get; }

    /// <summary>
    /// Every line received, capped at <see cref="MaxLines"/>; the source of truth.
    /// </summary>
    public ObservableCollection<ConsoleLine> Lines { get; } = [];

    /// <summary>
    /// The subset of <see cref="Lines"/> matching the current search; what the view binds to.
    /// </summary>
    public ObservableCollection<ConsoleLine> VisibleLines { get; } = [];

    /// <summary>The text search (proxied to the controller); narrows the visible lines.</summary>
    public string SearchText
    {
        get => Search.Query;
        set
        {
            if (Search.Query == value)
                return;

            Search.Query = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MatchSummary));
        }
    }

    /// <summary>Whether info/accent lines are shown (a severity filter). Off hides them from the view.</summary>
    [ObservableProperty]
    public partial bool ShowInfo { get; set; } = true;

    /// <summary>Whether warning lines are shown (a severity filter).</summary>
    [ObservableProperty]
    public partial bool ShowWarnings { get; set; } = true;

    /// <summary>Whether error/critical lines are shown (a severity filter).</summary>
    [ObservableProperty]
    public partial bool ShowErrors { get; set; } = true;

    /// <summary>Count of info/accent lines held (across the whole scrollback, not just the filtered view).</summary>
    public int InfoCount => _infoCount;

    /// <summary>Count of warning lines held.</summary>
    public int WarningCount => _warningCount;

    /// <summary>Count of error/critical lines held.</summary>
    public int ErrorCount => _errorCount;

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
    /// The view's text control, registered while attached. Selection and clipboard are view-level, so the
    /// copy/select-all actions (e.g. from the right-click menu) reach the on-screen text through this.
    /// Null while no view is attached — the actions no-op.
    /// </summary>
    public IConsoleTextInteraction? Text { get; set; }

    /// <summary>Whether the on-screen text has a selection (drives the Copy row's meaning: selection vs all).</summary>
    public bool HasSelection => Text?.HasSelection ?? false;

    /// <summary>
    /// Copies the current selection — or the whole console when nothing is selected — to the clipboard as
    /// plain text through the shared <see cref="Clipboard"/> service.
    /// </summary>
    public Task CopyAsync()
    {
        var text = Text?.CopyText();
        return string.IsNullOrEmpty(text) ? Task.CompletedTask : _clipboard.CopyText(text);
    }

    /// <summary>Selects every line.</summary>
    public void SelectAll() => Text?.SelectAll();

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
    public void Clear()
    {
        Lines.Clear();
        VisibleLines.Clear();
        _infoCount = _warningCount = _errorCount = 0;
        RaiseCounts();
        OnPropertyChanged(nameof(MatchSummary));
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnShowInfoChanged(bool value) => Search.Refresh();

    partial void OnShowWarningsChanged(bool value) => Search.Refresh();

    partial void OnShowErrorsChanged(bool value) => Search.Refresh();

    // A line shows when its severity's filter is on and it matches the text search. The three severity
    // filters bucket by weight — info/accent together, warning, error/critical — mirroring the counts.
    private bool Matches(ConsoleLine line) =>
        SeverityShown(line.Severity)
        && (string.IsNullOrEmpty(SearchText)
            || line.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    private bool SeverityShown(ConsoleSeverity severity) => severity switch
    {
        ConsoleSeverity.Error => ShowErrors,
        ConsoleSeverity.Warning => ShowWarnings,
        _ => ShowInfo,
    };

    // (UI thread) Swaps in the lines matching the current search + severity filters, in place.
    private void ApplyResults(IReadOnlyList<ConsoleLine> results)
    {
        VisibleLines.Reconcile(results);
        OnPropertyChanged(nameof(MatchSummary));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(InfoCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(ErrorCount));
    }

    private void Count(ConsoleSeverity severity, int delta)
    {
        switch (severity)
        {
            case ConsoleSeverity.Error:
                _errorCount += delta;
                break;
            case ConsoleSeverity.Warning:
                _warningCount += delta;
                break;
            default:
                _infoCount += delta;
                break;
        }
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

        // A single flooded flush can carry up to MaxLines pending lines, which would by itself fill the
        // entire scrollback. Trim the batch to the most recent MaxLines so AddLine's rolling-cap trim does
        // the rest — never blank existing history just because the batch is large.
        if (batch.Count > MaxLines)
            batch.RemoveRange(0, batch.Count - MaxLines);

        foreach (var line in batch)
            AddLine(line);

        RaiseCounts();
        OnPropertyChanged(nameof(MatchSummary));
        OnPropertyChanged(nameof(IsEmpty));
    }

    // Appends one line, mirrors it into the filtered view if it matches, keeps the per-severity counts, and
    // trims the oldest line from both collections (and its count) once the cap is reached.
    private void AddLine(ConsoleLine line)
    {
        Lines.Add(line);
        Count(line.Severity, +1);
        if (Matches(line))
            VisibleLines.Add(line);

        while (Lines.Count > MaxLines)
        {
            var removed = Lines[0];
            Lines.RemoveAt(0);
            Count(removed.Severity, -1);
            if (VisibleLines.Count > 0 && ReferenceEquals(VisibleLines[0], removed))
                VisibleLines.RemoveAt(0);
        }
    }
}
