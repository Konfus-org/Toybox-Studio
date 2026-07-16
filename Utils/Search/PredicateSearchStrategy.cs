namespace Toybox.Studio.Utils.Search;

/// <summary>
/// A strategy driven by a caller-supplied match predicate, for the cases the field-based
/// <see cref="SubstringSearchStrategy{T}"/> can't express — a row that always survives (a menu
/// separator), or a match that folds in more than text. Keeps candidates in their incoming order.
/// </summary>
public sealed class PredicateSearchStrategy<T> : ISearchStrategy<T>
{
    private readonly Func<T, string, bool> _matches;

    /// <param name="matches">Whether the candidate survives the (trimmed, non-empty) query.</param>
    public PredicateSearchStrategy(Func<T, string, bool> matches) => _matches = matches;

    public IReadOnlyList<T> Search(
        string query, IReadOnlyList<T> candidates, IProgress<double>? progress, CancellationToken ct)
    {
        var results = new List<T>(candidates.Count);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (_matches(candidate, query))
                results.Add(candidate);
        }

        return results;
    }
}
