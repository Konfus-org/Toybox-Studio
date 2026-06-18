using Toybox.Studio.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.Project;

namespace Toybox.Studio.Widgets.Dialogs;

/// <summary>
/// Backs the asset chooser: a searchable list of the assets matching a handle's type. The user can select
/// an asset, clear the reference (id 0), or cancel. The chosen outcome is exposed as <see cref="Result"/>,
/// which stays <c>(false, 0)</c> until the user commits. Holds no <see cref="Avalonia.Controls.Window"/>
/// reference; it raises <see cref="CloseRequested"/> so the host window closes itself.
/// </summary>
public sealed partial class AssetPickerViewModel : ObservableObject
{
    private readonly IReadOnlyList<Asset> _options;

    public AssetPickerViewModel(string title, IReadOnlyList<Asset> options, long currentId)
    {
        Title = title;
        // "None" is always the first option, so a reference can be cleared from the list itself (id 0). Any
        // stray id-0 entry in the source is dropped so it isn't duplicated.
        _options = options.Where(o => o.Id != 0).Prepend(new Asset(0, "None", "", "")).ToList();
        FilteredOptions = _options;
        SelectedAsset = _options.FirstOrDefault(o => o.Id == currentId);
        Refilter();
    }

    public string Title { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<Asset> FilteredOptions { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    public partial Asset? SelectedAsset { get; set; }

    [ObservableProperty]
    public partial bool IsGhostVisible { get; set; }

    [ObservableProperty]
    public partial string GhostMessage { get; set; } = "";

    /// <summary>The user's choice. Confirmed is false until they select or clear; on clear, Id is 0.</summary>
    public AssetPick Result { get; private set; }

    /// <summary>Raised when the dialog should close.</summary>
    public event Action? CloseRequested;

    partial void OnSearchTextChanged(string value) => Refilter();

    // Hide the matches behind a ghost whenever there's nothing to pick, so an empty picker reads as
    // intentional rather than broken — the message reflects whether the emptiness is a search miss or a
    // bare list.
    private void Refilter()
    {
        var query = SearchText?.Trim() ?? "";
        FilteredOptions = query.Length == 0
            ? _options
            : _options
                .Where(o => o.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        IsGhostVisible = FilteredOptions.Count == 0;
        GhostMessage = query.Length == 0 ? "Nothing to pick here." : "No matches.";
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void Select()
    {
        if (SelectedAsset is { } asset)
            Confirm(new AssetPick(true, asset.Id));
    }

    private bool CanSelect() => SelectedAsset is not null;

    [RelayCommand]
    private void Clear() => Confirm(new AssetPick(true, 0));

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    private void Confirm(AssetPick pick)
    {
        Result = pick;
        CloseRequested?.Invoke();
    }
}
