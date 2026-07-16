using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;

namespace Toybox.Studio.AssetOwners;

/// <summary>
/// The shared state of every panel that owns an editable asset (or asset-like document): the
/// unsaved-changes star on <see cref="Title"/>, the Save/Cancel pair the <see cref="AssetOwnerView"/>
/// renders at its bottom (Save enabled only while dirty, Cancel raising <see cref="CloseRequested"/>
/// for whoever opened the panel to close it), and the <see cref="Body"/> content shown above them.
/// Owners hosting a real engine <see cref="Asset"/> call <see cref="Host"/> and get the rest for free —
/// <see cref="IsDirty"/> follows the asset's own dirty flag, Save persists through its
/// <see cref="Asset.SaveAsync"/>, and Undo/Redo walk an <see cref="EditHistory"/> of whole-body snapshots
/// (seeded once the asset loads, one step per content edit, restored by pushing the snapshot back to the
/// engine); owners over other documents (the Settings panel) set <see cref="IsDirty"/> themselves,
/// override <see cref="PersistAsync"/>, and leave the history inactive.
/// </summary>
public abstract partial class AssetOwnerViewModel : ObservableObject, IUndoTarget
{
    private readonly EditHistory _history = new();
    private Asset? _hosted;

    // Set while a Undo/Redo restore is pushing snapshot deltas, so the resulting edits don't record
    // themselves back into the history as fresh steps.
    private bool _restoring;

    // Nonzero while a continuous interactive edit (a gizmo drag) is in flight: its inbound applies land
    // on the mirror as they arrive, but the resulting history step is deferred to EndEditTransaction so
    // the whole run collapses into one undo.
    private int _transactionDepth;

    protected AssetOwnerViewModel() => _history.Changed += OnHistoryChanged;

    /// <summary>Raised when Cancel asks the hosting panel to close (the opener owns the window or
    /// dock slot, so it is the one that closes it).</summary>
    public event Action? CloseRequested;

    /// <summary>Raised when <see cref="CanUndo"/>/<see cref="CanRedo"/> may have changed, so the workspace
    /// menu can refresh Undo/Redo for the focused document (<see cref="IUndoTarget"/>).</summary>
    public event Action? UndoStateChanged;

    [ObservableProperty]
    public partial bool IsDirty { get; protected set; }

    /// <summary>The panel title, carrying the unsaved-changes star.</summary>
    public string Title => IsDirty ? $"{DisplayName} *" : DisplayName;

    /// <summary>The title without the dirty star (the hosted asset's name, "Settings", …).</summary>
    protected abstract string DisplayName { get; }

    /// <summary>The owned content the <see cref="AssetOwnerView"/> hosts above its Save/Cancel bar (a
    /// property grid's view-model, …); the concrete owner's view supplies the DataTemplate resolving
    /// it to its view.</summary>
    public abstract object? Body { get; }

    /// <summary>Whether an earlier content edit can be undone (the history has a step behind the present).</summary>
    public bool CanUndo => _history.CanUndo;

    /// <summary>Whether an undone content edit can be reapplied.</summary>
    public bool CanRedo => _history.CanRedo;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task SaveAsync()
    {
        // A failed persist keeps the dirty star (and the enabled Save) — nothing landed.
        if (await PersistAsync().ContinueOnSameContext())
        {
            IsDirty = false;
            _history.MarkSaved();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Restore(_history.Undo());

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Restore(_history.Redo());

    // IUndoTarget routes the focused-document menu/keybindings through the same commands the bar buttons use.
    void IUndoTarget.Undo() => UndoCommand.Execute(null);

    void IUndoTarget.Redo() => RedoCommand.Execute(null);

    /// <summary>
    /// Opens an edit transaction: content edits that land while it is open don't each record a history
    /// step — the whole run collapses into the single step <see cref="EndEditTransaction"/> records. This
    /// is how a continuous interactive edit (a gizmo drag streams a burst of position/rotation/scale
    /// applies) becomes one undo. Nestable; every call pairs with an <see cref="EndEditTransaction"/>.
    /// </summary>
    public void BeginEditTransaction() => _transactionDepth++;

    /// <summary>
    /// Closes the innermost <see cref="BeginEditTransaction"/> and, once the outermost closes, records the
    /// net change as one undo step and flags the asset dirty — unless nothing actually changed (a
    /// click-release on a gizmo handle with no drag), which is left as a no-op. An unbalanced call (no
    /// open transaction) is ignored.
    /// </summary>
    public void EndEditTransaction()
    {
        if (_transactionDepth == 0 || --_transactionDepth > 0 || _hosted is not { } asset)
            return;

        // The edit arrived through inbound engine applies, which bypass the setter's edit path (so nothing
        // recorded or flagged dirty as it streamed). Record the net body as one step and, only when it
        // genuinely changed, mark the asset dirty so its star shows and Save persists it.
        if (_history.Record(asset.Serialize()))
            asset.MarkBodyEdited();
    }

    /// <summary>Persists the owned content; the default saves the hosted asset.</summary>
    protected virtual async Task<Result> PersistAsync() =>
        _hosted is { } asset
            ? await asset.SaveAsync().ContinueOnSameContext()
            : Result.Ok();

    /// <summary>Hands the owner the engine asset it edits (null to release it): from here
    /// <see cref="IsDirty"/> tracks the asset's own dirty flag, Save is its save, and Undo/Redo walk its
    /// content edits (the history seeds itself once the asset finishes loading).</summary>
    protected void Host(Asset? asset)
    {
        if (_hosted is { } previous)
        {
            previous.Changed -= OnHostedChanged;
            previous.Edited -= OnHostedEdited;
        }

        _hosted = asset;
        if (asset is not null)
        {
            asset.Changed += OnHostedChanged;
            asset.Edited += OnHostedEdited;
            _ = SeedHistoryAsync(asset);
        }
        IsDirty = asset?.IsDirty is true;
    }

    private void OnHostedChanged() => IsDirty = _hosted?.IsDirty is true;

    // One content edit (excludes inbound engine applies, renames, and our own restore pushes): snapshot
    // the asset's body as the new present, coalescing a run of edits to the same property into one step.
    // Inside an edit transaction the step is held — the whole run becomes the one EndEditTransaction records.
    private void OnHostedEdited(EditInfo edit)
    {
        if (_restoring || _transactionDepth > 0 || _hosted is not { } asset)
            return;

        _history.Record(asset.Serialize(), coalesceKey: edit.Key);
    }

    // Pushes an undo/redo step onto the live asset (which reaches the engine and re-flags dirty), guarded
    // so the resulting edits don't record themselves as new history steps. Only the properties this edit
    // actually changed (Target vs From differ) are pushed — never a value that merely drifted in the mirror
    // since the snapshot (a world's globals, another entity's engine-driven motion), which a blanket
    // restore would wrongly drive back into the engine.
    private void Restore((JObject Target, JObject From)? step)
    {
        if (step is not { } move || _hosted is not { } asset)
            return;

        _restoring = true;
        try
        {
            asset.RestoreDiff(move.Target, move.From);
        }
        finally
        {
            _restoring = false;
        }
    }

    // Seeds the timeline with the asset's loaded body as the baseline. Awaiting Loaded first means an
    // existing asset's hydrated values are the baseline (a fresh asset is already loaded, so its defaults
    // are); a swap before it completes abandons the stale seed.
    private async Task SeedHistoryAsync(Asset asset)
    {
        await asset.Loaded.ContinueOnSameContext();
        if (ReferenceEquals(_hosted, asset))
            _history.Reset(asset.Serialize());
    }

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        UndoStateChanged?.Invoke();
    }

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(Title));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
