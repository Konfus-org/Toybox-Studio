namespace Toybox.Studio.Services.World;

/// <summary>
/// Shares the currently selected entity between widgets as a stable entity id (not an object reference), so
/// the selection survives a world refresh that rebuilds the entity snapshots.
/// </summary>
public sealed class WorldSelection
{
    public ulong? SelectedId { get; private set; }

    public event Action<ulong?>? SelectionChanged;

    public void Select(ulong? id)
    {
        if (SelectedId == id)
            return;

        SelectedId = id;
        SelectionChanged?.Invoke(id);
    }
}
