using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Assets;
using Toybox.Studio.Utils;

namespace Toybox.Studio.AssetOwners;

/// <summary>
/// The shared state of every panel that owns an editable asset (or asset-like document): the
/// unsaved-changes star on <see cref="Title"/>, the Save/Cancel pair the <see cref="AssetOwnerView"/>
/// renders at its bottom (Save enabled only while dirty, Cancel raising <see cref="CloseRequested"/>
/// for whoever opened the panel to close it), and the <see cref="Body"/> content shown above them.
/// Owners hosting a real engine <see cref="Asset"/> call <see cref="Host"/> and get the rest for free —
/// <see cref="IsDirty"/> follows the asset's own dirty flag and Save persists through its
/// <see cref="Asset.SaveAsync"/>; owners over other documents (the Settings draft) set
/// <see cref="IsDirty"/> themselves and override <see cref="PersistAsync"/>.
/// </summary>
public abstract partial class AssetOwnerViewModel : ObservableObject
{
    private Asset? _hosted;

    /// <summary>Raised when Cancel asks the hosting panel to close (the opener owns the window or
    /// dock slot, so it is the one that closes it).</summary>
    public event Action? CloseRequested;

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

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    [RelayCommand(CanExecute = nameof(IsDirty))]
    private async Task SaveAsync()
    {
        // A failed persist keeps the dirty star (and the enabled Save) — nothing landed.
        if (await PersistAsync().ContinueOnSameContext())
            IsDirty = false;
    }

    /// <summary>Persists the owned content; the default saves the hosted asset.</summary>
    protected virtual async Task<Result> PersistAsync() =>
        _hosted is { } asset
            ? await asset.SaveAsync().ContinueOnSameContext()
            : Result.Ok();

    /// <summary>Hands the owner the engine asset it edits (null to release it): from here
    /// <see cref="IsDirty"/> tracks the asset's own dirty flag and Save is its save.</summary>
    protected void Host(Asset? asset)
    {
        if (_hosted is { } previous)
            previous.Changed -= OnHostedChanged;

        _hosted = asset;
        if (asset is not null)
            asset.Changed += OnHostedChanged;
        IsDirty = asset?.IsDirty is true;
    }

    private void OnHostedChanged() => IsDirty = _hosted?.IsDirty is true;

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(Title));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
