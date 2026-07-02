using System;
using Toybox.Assets;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// The strongly-typed payload of an <see cref="Asset{TData}"/> — the raw RPC reflection of an engine asset's
/// editable body. It IS a <see cref="EngineSyncedObject"/>: its <c>[EngineSync]</c> fields are the typed, buffered
/// view of the body that the inspector and <see cref="Asset.SaveAsync"/> round-trip, while the asset wrapper
/// around it (<see cref="Asset{TData}"/>) owns identity and file lifecycle. Splitting the two keeps "what file is
/// this" (the asset) apart from "what's in it" (the data).
///
/// It also owns the asset's <b>dirty</b> state, tracked automatically: any reflected-field edit routes through
/// <see cref="EngineSyncedObject.OnFieldEdited"/>, which marks it dirty — so no caller (view-model or otherwise)
/// flags edits by hand. A subclass whose edits shouldn't always count (a world edited while the game plays)
/// gates them through <see cref="CanDirty"/>.
/// </summary>
public abstract class AssetData : EngineSyncedObject
{
    /// <summary>The raw text payload for a plain-text asset (a script's / shader's source) — the
    /// <see cref="AssetFormat.PlainText"/> counterpart of the reflected JSON body. Null for a JSON-format asset
    /// (whose data is its <c>[EngineSync]</c> fields instead). The <see cref="Asset{TData}"/> reads/writes it
    /// directly from the source file, with no JSON in the asset layer.</summary>
    public string? Text { get; set; }

    /// <summary>Whether the asset holds unsaved editor changes.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Called once when this payload is bound to its asset handle, with the asset's file extension (no
    /// dot, e.g. "vert"). Lets a payload derive a field the engine body doesn't carry — a <see cref="Shader"/>'s
    /// pipeline <see cref="Shader.Type"/> comes from its extension. Default no-op.</summary>
    internal virtual void OnHandleBound(string extension) { }

    /// <summary>Raised whenever <see cref="IsDirty"/> changes.</summary>
    public event Action<bool>? DirtyChanged;

    /// <summary>Whether an edit right now should mark the asset dirty. Overridden by a subclass that has moments
    /// where edits don't count (e.g. a world edited against the engine's throwaway play snapshot).</summary>
    protected virtual bool CanDirty => true;

    /// <summary>Binds this payload to a running world it streams into, so reflected-field edits push live to the
    /// engine's resident copy (a <see cref="StreamDestination.Preview"/> world or the <see cref="StreamDestination.Game"/>
    /// world the viewports render). Edits still buffer for Save.</summary>
    internal void BindToStream(StreamDestination destination, ulong assetId, IEngineSyncScheduler scheduler) =>
        Bind(EngineAddress.Stream(destination, assetId), scheduler);

    /// <summary>Marks the asset dirty after an edit (subject to <see cref="CanDirty"/>). Called automatically for
    /// this object's own field edits; a container (a world) also calls it when one of its nested reflected
    /// children is edited.</summary>
    protected internal void MarkDirty()
    {
        if (CanDirty)
            SetDirty(true);
    }

    /// <summary>Clears the dirty flag (a fresh load / save starts clean).</summary>
    public void ClearDirty() => SetDirty(false);

    // An edit to one of this asset's own reflected fields dirties it (a no-op during hydration, which is
    // suppressed and so never reaches here).
    protected override void OnFieldEdited(string wire) => MarkDirty();

    private void SetDirty(bool value)
    {
        if (IsDirty == value)
            return;
        IsDirty = value;
        DirtyChanged?.Invoke(value);
    }
}
