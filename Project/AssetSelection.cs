using System;

namespace Toybox.Studio.Project;

/// <summary>
/// Shares the currently-inspected asset between the Asset Browser (which sets it when a tile is selected) and the
/// Inspector (which shows the asset's editable properties when one is set). Mutually exclusive with the entity
/// selection: selecting an asset clears the world selection and vice-versa, so the single Inspector shows one
/// thing at a time. Held as an <see cref="AssetHandle"/> so it survives a catalog refresh.
/// </summary>
public sealed class AssetSelection
{
    public AssetHandle Current { get; private set; } = AssetHandle.None;

    /// <summary>Raised whenever the selected asset changes (including being cleared).</summary>
    public event Action? Changed;

    public bool HasSelection => !Current.IsNone;

    public void Select(AssetHandle handle)
    {
        if (Current.Id == handle.Id
            && string.Equals(Current.Path, handle.Path, StringComparison.OrdinalIgnoreCase))
            return;

        Current = handle;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (Current.IsNone)
            return;

        Current = AssetHandle.None;
        Changed?.Invoke();
    }
}
