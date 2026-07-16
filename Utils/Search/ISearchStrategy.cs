namespace Toybox.Studio.Utils.Search;

/// <summary>
/// A pluggable, thread-safe query algorithm: given a query and a candidate set, it matches and ranks
/// them into a result list. This is the unit a feature adds to get fast, non-blocking search — the
/// <see cref="SearchController{T}"/> runs it off the UI thread, debounced and cancellable.
///
/// Implementations must be pure with respect to the arguments (no UI or shared mutable state), so the
/// controller can run and cancel them freely on the thread pool. Honour <paramref name="ct"/> — a
/// newer keystroke cancels an in-flight search — and report fractional progress through
/// <paramref name="progress"/> when the work is long enough to be worth showing (cheap in-memory
/// strategies leave it unreported and the search field just spins).
/// </summary>
public interface ISearchStrategy<T>
{
    /// <param name="query">The trimmed, non-empty query (the controller short-circuits empty queries).</param>
    /// <param name="candidates">The snapshot to search, read on the UI thread before this runs.</param>
    /// <param name="progress">Optional 0..1 progress sink; always supplied, safe to ignore.</param>
    /// <param name="ct">Cancels when a newer query supersedes this one.</param>
    IReadOnlyList<T> Search(
        string query, IReadOnlyList<T> candidates, IProgress<double>? progress, CancellationToken ct);
}
