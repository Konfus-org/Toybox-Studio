namespace Toybox.Studio.Utils.Search;

/// <summary>
/// The default strategy: keeps a candidate when any of its projected fields contains the query
/// (case-insensitive substring), preserving the candidates' incoming order. This is the drop-in for
/// the common "filter a list by a few text fields" case — the exact idiom the panels filtered with
/// before, now off the UI thread. Cheap, so it never reports progress.
/// </summary>
public sealed class SubstringSearchStrategy<T> : ISearchStrategy<T>
{
    private readonly Func<T, string?>[] _fields;

    /// <param name="fields">One or more selectors for the text a candidate is matched on (e.g. name,
    /// type, path). At least one is required.</param>
    public SubstringSearchStrategy(params Func<T, string?>[] fields)
    {
        if (fields.Length == 0)
            throw new ArgumentException("At least one field selector is required.", nameof(fields));

        _fields = fields;
    }

    public IReadOnlyList<T> Search(
        string query, IReadOnlyList<T> candidates, IProgress<double>? progress, CancellationToken ct)
    {
        var results = new List<T>(candidates.Count);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (Matches(candidate, query))
                results.Add(candidate);
        }

        return results;
    }

    private bool Matches(T candidate, string query)
    {
        foreach (var field in _fields)
        {
            var value = field(candidate);
            if (!string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
