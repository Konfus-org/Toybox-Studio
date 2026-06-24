using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services.Dialogs;

namespace Toybox.Studio.Widgets.Dialogs;

/// <summary>
/// Backs the type chooser: a searchable list of <see cref="CatalogItem"/>s (component types, script assets,
/// …) the user picks one of, or cancels. The chosen item is exposed as <see cref="Result"/>, which stays null
/// until the user commits. Holds no <see cref="Avalonia.Controls.Window"/> reference; it raises
/// <see cref="CloseRequested"/> so the host window closes itself.
/// </summary>
public sealed partial class CatalogPickerViewModel : ObservableObject
{
    private readonly IReadOnlyList<CatalogItem> _options;
    private readonly string _emptyMessage;

    public CatalogPickerViewModel(string title, string emptyMessage, IReadOnlyList<CatalogItem> options)
    {
        Title = title;
        _emptyMessage = emptyMessage;
        _options = options;
        FilteredOptions = options;
        Refilter();
    }

    /// <summary>Raised when the dialog should close.</summary>
    public event Action? CloseRequested;

    public string Title { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<CatalogItem> FilteredOptions { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    public partial CatalogItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool IsGhostVisible { get; set; }

    [ObservableProperty]
    public partial string GhostMessage { get; set; } = "";

    /// <summary>The user's choice, or null until they select one.</summary>
    public CatalogItem? Result { get; private set; }

    partial void OnSearchTextChanged(string value) => Refilter();

    // Hide the list behind a ghost whenever there's nothing to pick, so an empty picker reads as intentional;
    // the message distinguishes a bare list (nothing to add) from a search miss.
    private void Refilter()
    {
        var query = SearchText?.Trim() ?? "";
        FilteredOptions = query.Length == 0
            ? _options
            : _options
                .Where(option => option.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        IsGhostVisible = FilteredOptions.Count == 0;
        GhostMessage = query.Length == 0 ? _emptyMessage : "No matches.";
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void Select()
    {
        if (SelectedItem is { } item)
            Confirm(item);
    }

    private bool CanSelect() => SelectedItem is not null;

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    private void Confirm(CatalogItem item)
    {
        Result = item;
        CloseRequested?.Invoke();
    }
}
