using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.Utils.Search;

/// <summary>
/// The non-blocking query pump a view-model composes to drive a search field. Setting <see cref="Query"/>
/// (or calling <see cref="Refresh"/> when the underlying data changes) coalesces keystrokes over a short
/// debounce, snapshots the source on the UI thread, then runs the <see cref="ISearchStrategy{T}"/> on the
/// thread pool. Results are handed back on the UI thread; a newer query cancels the in-flight one so stale
/// results never overwrite newer ones. <see cref="IsBusy"/> drives the field's spinner and
/// <see cref="Progress"/> its determinate arc.
///
/// The debounce/cancel/latest-wins shape mirrors <c>SyncScheduler</c>; failures are routed through
/// <see cref="TaskExtensions.FireAndForget(Task, Action{Exception})"/> (the app logger), so a faulted
/// search can never block the editor or fault the process. An empty query short-circuits to "show all"
/// (the snapshot, applied synchronously) — no background hop.
/// </summary>
public sealed partial class SearchController<T> : ObservableObject, IDisposable
{
    // Long enough to swallow a burst of keystrokes, short enough to feel immediate.
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(120);

    private readonly ISearchStrategy<T> _strategy;
    private readonly Func<IReadOnlyList<T>> _snapshot;
    private readonly Action<IReadOnlyList<T>> _onResults;
    private readonly DispatcherTimer _debounce;

    // The in-flight run's cancellation source; a new run cancels and replaces it. Late results whose
    // source is no longer the current one are ignored.
    private CancellationTokenSource? _running;
    private string _query = "";
    private bool _disposed;

    /// <param name="strategy">How candidates are matched and ranked (runs off the UI thread).</param>
    /// <param name="snapshot">Reads the current candidate set; invoked on the UI thread each run, so it
    /// may safely touch observable state.</param>
    /// <param name="onResults">Applies a finished result list; invoked on the UI thread.</param>
    public SearchController(
        ISearchStrategy<T> strategy, Func<IReadOnlyList<T>> snapshot, Action<IReadOnlyList<T>> onResults)
    {
        _strategy = strategy;
        _snapshot = snapshot;
        _onResults = onResults;
        _debounce = new DispatcherTimer { Interval = DebounceInterval };
        _debounce.Tick += OnDebounceTick;
    }

    /// <summary>Whether a query is pending or running — bind the field's spinner to this. Set by the
    /// controller only (bindings read it).</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Fractional progress (0..1) reported by the strategy, or <c>null</c> when it reports
    /// nothing (the spinner stays indeterminate). Reset to <c>null</c> when a run completes.</summary>
    [ObservableProperty]
    public partial double? Progress { get; set; }

    /// <summary>The live query. Setting it schedules a debounced search.</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value))
                Schedule();
        }
    }

    /// <summary>Re-runs the current query — call when the source data or filter parameters change
    /// (a catalog refresh, a category switch, a severity toggle).</summary>
    public void Refresh() => Schedule();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _debounce.Stop();
        _debounce.Tick -= OnDebounceTick;
        _running?.Cancel();
    }

    // (UI thread) restart the debounce window; the run fires once it settles.
    private void Schedule()
    {
        if (_disposed)
            return;

        IsBusy = true;
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce.Stop();
        Run();
    }

    // (UI thread) supersede any in-flight run, snapshot the source here, then filter off-thread.
    private void Run()
    {
        _running?.Cancel();
        var cts = new CancellationTokenSource();
        _running = cts;

        var query = _query.Trim();
        var candidates = _snapshot();

        // Empty query means "show everything": apply the snapshot straight away, no background hop.
        if (query.Length == 0)
        {
            Apply(candidates, cts);
            return;
        }

        var progress = new CoalescingProgress(value => Dispatch.To(DispatchContext.UI, () =>
        {
            if (!cts.IsCancellationRequested && ReferenceEquals(_running, cts))
                Progress = value;
        }));

        Task.Run(() =>
        {
            try
            {
                var results = _strategy.Search(query, candidates, progress, cts.Token);
                Dispatch.To(DispatchContext.UI, () => Apply(results, cts));
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer query — the newer run owns the results.
            }
        }).FireAndForget();
    }

    // (UI thread) apply a run's results unless it has been superseded.
    private void Apply(IReadOnlyList<T> results, CancellationTokenSource cts)
    {
        if (_disposed || cts.IsCancellationRequested || !ReferenceEquals(_running, cts))
            return;

        _onResults(results);
        IsBusy = false;
        Progress = null;
    }

    /// <summary>
    /// Throttles a strategy's progress reports to at most one per <see cref="MinIntervalMs"/> (latest value
    /// wins), so a strategy reporting in a tight loop can't flood the dispatcher. Same leading-edge +
    /// trailing-timer shape as <c>SyncScheduler</c>. The sink itself marshals to the UI thread.
    /// </summary>
    private sealed class CoalescingProgress : IProgress<double>
    {
        private const int MinIntervalMs = 40;

        private readonly Action<double> _sink;
        private readonly object _gate = new();
        private long _lastSent = long.MinValue;
        private double _pending;
        private Timer? _trailing;

        public CoalescingProgress(Action<double> sink) => _sink = sink;

        public void Report(double value)
        {
            lock (_gate)
            {
                // A trailing send is already scheduled: the newest value just replaces its payload.
                if (_trailing is not null)
                {
                    _pending = value;
                    return;
                }

                var now = Environment.TickCount64;
                if (now - _lastSent < MinIntervalMs)
                {
                    _pending = value;
                    var due = (int)Math.Max(1, MinIntervalMs - (now - _lastSent));
                    _trailing = new Timer(_ => FireTrailing(), null, due, Timeout.Infinite);
                    return;
                }

                _lastSent = now;
            }

            _sink(value);
        }

        private void FireTrailing()
        {
            double value;
            lock (_gate)
            {
                _trailing?.Dispose();
                _trailing = null;
                _lastSent = Environment.TickCount64;
                value = _pending;
            }

            _sink(value);
        }
    }
}
