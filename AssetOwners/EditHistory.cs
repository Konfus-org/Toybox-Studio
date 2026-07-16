using Newtonsoft.Json.Linq;

namespace Toybox.Studio.AssetOwners;

/// <summary>
/// A bounded undo/redo history of whole-body snapshots for one editable target (an asset's serialized
/// body). The list is the full timeline and <see cref="_cursor"/> marks the present; <see cref="Undo"/>/
/// <see cref="Redo"/> walk it, and a fresh <see cref="Record"/> past the present truncates the redo tail.
/// Consecutive edits sharing a coalesce key within a short window collapse into one step (a gizmo drag or
/// a slider scrub becomes a single undo), and a saved-point marker keeps the first edit after a save from
/// merging back into the saved snapshot.
/// </summary>
public sealed class EditHistory
{
    // A run of same-key edits closer together than this coalesces into one step; a longer gap starts a
    // new one, so "nudge, pause, nudge" is two undos while a continuous drag is one.
    private const int CoalesceWindowMs = 500;

    private readonly List<Entry> _entries = [];
    private readonly int _limit;
    private int _cursor = -1;
    private int _savedCursor = -1;
    private long _lastRecordAt;

    /// <param name="limit">The most steps kept; the oldest are dropped past it.</param>
    public EditHistory(int limit = 100) => _limit = limit;

    /// <summary>Raised whenever the timeline shape changes (record/undo/redo/reset/save), so the owner can
    /// refresh its undo/redo command state.</summary>
    public event Action? Changed;

    public bool CanUndo => _cursor > 0;

    public bool CanRedo => _cursor >= 0 && _cursor < _entries.Count - 1;

    /// <summary>Whether the present snapshot is the one last saved (or the loaded baseline).</summary>
    public bool IsAtSavedPoint => _cursor == _savedCursor;

    /// <summary>Seeds the history with the loaded body as the present (and the saved point); any prior
    /// timeline is dropped.</summary>
    public void Reset(JObject baseline)
    {
        _entries.Clear();
        _entries.Add(new Entry(Clone(baseline), null));
        _cursor = 0;
        _savedCursor = 0;
        Changed?.Invoke();
    }

    /// <summary>
    /// Records an edit's resulting body as the new present, returning whether the timeline actually
    /// changed. A record whose <paramref name="coalesceKey"/> matches the present entry's within the
    /// coalesce window replaces it in place (the run collapses to one step); otherwise it truncates any
    /// redo tail and pushes a new step. A body identical to the present is not an edit and is ignored
    /// (returns false). Recording before any <see cref="Reset"/> seeds the history from this body.
    /// </summary>
    public bool Record(JObject body, object? coalesceKey = null)
    {
        if (_cursor < 0)
        {
            Reset(body);
            return true;
        }

        // A body identical to the present isn't an edit — a zero-delta gizmo drag, or a value set back to
        // what it already held — so it neither records a step nor branches the timeline.
        if (JToken.DeepEquals(_entries[_cursor].Body, body))
            return false;

        var now = Environment.TickCount64;
        var snapshot = Clone(body);

        // A continued run to the same target overwrites the present value rather than adding a step —
        // but never the saved snapshot (that would make undoing back to it impossible).
        var continues = coalesceKey is not null
            && _cursor != _savedCursor
            && now - _lastRecordAt <= CoalesceWindowMs
            && _entries[_cursor].CoalesceKey is { } present
            && Equals(present, coalesceKey);
        _lastRecordAt = now;

        if (continues)
        {
            _entries[_cursor] = new Entry(snapshot, coalesceKey);
            Changed?.Invoke();
            return true;
        }

        // The timeline branches here: drop any redo tail (and the saved point if it lived in that
        // now-unreachable future).
        if (_cursor < _entries.Count - 1)
        {
            if (_savedCursor > _cursor)
                _savedCursor = -1;
            _entries.RemoveRange(_cursor + 1, _entries.Count - 1 - _cursor);
        }

        _entries.Add(new Entry(snapshot, coalesceKey));
        _cursor++;
        Trim();
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Steps back one snapshot, returning both the body to restore to (<c>Target</c>) and the one being
    /// left (<c>From</c>), or null when there is nothing to undo. The pair lets the caller push back only
    /// the properties this edit actually changed (Target vs From differ), leaving everything else — which
    /// may have drifted in the live mirror since the snapshot — untouched.
    /// </summary>
    public (JObject Target, JObject From)? Undo()
    {
        if (!CanUndo)
            return null;

        var from = _entries[_cursor].Body;
        _cursor--;
        Changed?.Invoke();
        return (Clone(_entries[_cursor].Body), Clone(from));
    }

    /// <summary>Steps forward one snapshot, returning the body to restore to and the one being left, or
    /// null when there is nothing to redo (see <see cref="Undo"/> for why both).</summary>
    public (JObject Target, JObject From)? Redo()
    {
        if (!CanRedo)
            return null;

        var from = _entries[_cursor].Body;
        _cursor++;
        Changed?.Invoke();
        return (Clone(_entries[_cursor].Body), Clone(from));
    }

    /// <summary>Marks the present snapshot as the saved one (called after a successful save), so the next
    /// edit starts a fresh step instead of coalescing into it.</summary>
    public void MarkSaved()
    {
        _savedCursor = _cursor;
        Changed?.Invoke();
    }

    // Snapshots are handed out and taken in by reference, so every store and return clones — the timeline
    // stays immune to later mutation of a body a caller still holds.
    private static JObject Clone(JObject body) => (JObject)body.DeepClone();

    // Keeps the newest _limit steps; dropping the oldest shifts both cursors down with it (the saved
    // cursor going negative once its step is trimmed, which simply means "no reachable saved point").
    private void Trim()
    {
        var excess = _entries.Count - _limit;
        if (excess <= 0)
            return;

        _entries.RemoveRange(0, excess);
        _cursor -= excess;
        _savedCursor -= excess;
    }

    private readonly record struct Entry(JObject Body, object? CoalesceKey);
}
