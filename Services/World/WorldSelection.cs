namespace Toybox.Studio.Services.World;

/// <summary>
/// Shares the current entity selection between widgets as stable entity ids (not object references), so the
/// selection survives a world refresh that rebuilds the entity snapshots. Selection is a set to support
/// multi-select (Shift-click in the viewport, marquee box-select); <see cref="PrimaryId"/> is the active
/// entity — the last one added — which single-selection consumers (the inspector, local-space gizmo) follow.
/// </summary>
public sealed class WorldSelection
{
    // Ordered: the last entry is the active/primary selection. A list (not a set) keeps that ordering and the
    // selections are always tiny, so linear Contains/Remove is fine.
    private readonly List<ulong> _selected = [];

    /// <summary>Raised whenever the selection set changes; subscribers read <see cref="SelectedIds"/>/
    /// <see cref="PrimaryId"/>.</summary>
    public event Action? SelectionChanged;

    public IReadOnlyList<ulong> SelectedIds => _selected;

    /// <summary>The active entity — the last one added — or null when nothing is selected.</summary>
    public ulong? PrimaryId => _selected.Count > 0 ? _selected[^1] : null;

    /// <summary>Back-compat alias of <see cref="PrimaryId"/> for single-selection consumers.</summary>
    public ulong? SelectedId => PrimaryId;

    /// <summary>True while the world view rebuilds its selector rows (a reconcile). The trees raise transient
    /// SelectionChanged as rows are removed and re-added; the tree's selection adapter checks this to ignore
    /// those structural changes as non-user edits. Toggled by <see cref="BeginBatch"/>/<see cref="EndBatch"/>;
    /// the selection set itself is unaffected.</summary>
    public bool IsBatching { get; private set; }

    public void BeginBatch() => IsBatching = true;

    public void EndBatch() => IsBatching = false;

    public bool Contains(ulong id) => _selected.Contains(id);

    /// <summary>Replaces the whole selection with the single id, or clears it when null. Used by the tree
    /// (a plain click) and a plain viewport click.</summary>
    public void Select(ulong? id)
    {
        if (id is { } value)
            Set(value);
        else
            Clear();
    }

    /// <summary>Replaces the selection with exactly this id.</summary>
    public void Set(ulong id)
    {
        if (_selected.Count == 1 && _selected[0] == id)
            return;

        _selected.Clear();
        _selected.Add(id);
        SelectionChanged?.Invoke();
    }

    /// <summary>Adds an id to the selection (becoming primary), a no-op if already the primary.</summary>
    public void Add(ulong id)
    {
        if (PrimaryId == id)
            return;

        _selected.Remove(id);
        _selected.Add(id);
        SelectionChanged?.Invoke();
    }

    /// <summary>Toggles an id in/out of the selection (Shift-click). Removing keeps the rest; adding makes it
    /// primary.</summary>
    public void Toggle(ulong id)
    {
        if (_selected.Remove(id))
            SelectionChanged?.Invoke();
        else
            Add(id);
    }

    /// <summary>Replaces the selection with the given set (marquee box-select), preserving order so the last
    /// id becomes primary. A no-op when the set is already identical.</summary>
    public void SetMany(IReadOnlyList<ulong> ids)
    {
        if (_selected.SequenceEqual(ids))
            return;

        _selected.Clear();
        _selected.AddRange(ids);
        SelectionChanged?.Invoke();
    }

    public void Clear()
    {
        if (_selected.Count == 0)
            return;

        _selected.Clear();
        SelectionChanged?.Invoke();
    }
}
