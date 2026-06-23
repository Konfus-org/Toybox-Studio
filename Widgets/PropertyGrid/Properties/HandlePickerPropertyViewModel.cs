using Toybox.Studio.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Project;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A handle/asset reference rendered as a link plus a picker button: the current asset shows as a
/// hyperlink of its resolved name (clicking reveals it in the asset database), and the picker button
/// opens a modal chooser of assets matching the node's <c>$choices</c> type list, committing the chosen
/// asset's id back to the backing token. Used for [[editor::view("handle"/"asset")]].
/// </summary>
public sealed partial class HandlePickerPropertyViewModel : PropertyViewModel
{
    private readonly AssetCatalog? _catalog;
    private readonly IReadOnlyList<string>? _typeFilter;
    private readonly JsonValueSlot _slot;

    [ObservableProperty]
    private string _displayName;

    public HandlePickerPropertyViewModel(PropertyNode node, Action? commit, AssetCatalog? catalog) : base(node)
    {
        _catalog = catalog;
        _typeFilter = node.Choices;
        _slot = new JsonValueSlot(node.Value);
        CommitChanges = commit;
        _displayName = ResolveDisplayName();

        if (catalog is not null)
            catalog.Changed += OnCatalogUpdated;
    }

    public long CurrentId => _slot.Read<long?>() ?? 0;

    public bool HasReference => CurrentId != 0;

    private void OnCatalogUpdated() => Dispatch.To(DispatchContext.UI, RefreshDisplay);

    private void RefreshDisplay()
    {
        DisplayName = ResolveDisplayName();
        OnPropertyChanged(nameof(HasReference));
    }

    private string ResolveDisplayName()
    {
        var id = CurrentId;
        if (id == 0)
            return "None";

        return _catalog?.ResolveName(id) ?? $"#{id}";
    }

    /// <summary>
    /// Clicking the reference label: a set reference reveals itself in the catalog; a "None" reference opens
    /// the asset chooser so the user can pick a real asset.
    /// </summary>
    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (HasReference)
            Open();
        else
            await PickAsync().ContinueOnSameContext();
    }

    /// <summary>Reveals the referenced asset in the catalog (the hyperlink action).</summary>
    [RelayCommand]
    private void Open()
    {
        if (CurrentId != 0)
            _catalog?.Activate(CurrentId);
    }

    /// <summary>Opens the modal asset chooser filtered to this handle's type, then commits the pick.</summary>
    [RelayCommand]
    private async Task PickAsync()
    {
        var options = _catalog?.AssetsOfType(_typeFilter) ?? [];
        var title = _typeFilter is { Count: > 0 }
            ? $"Select {string.Join(" / ", _typeFilter)}"
            : "Select asset";

        var pick = await AssetPicker.ShowAsync(title, options, CurrentId).ContinueOnSameContext();
        if (!pick.Confirmed)
            return;

        if (_slot.Set(new JValue(pick.Id)))
        {
            RefreshDisplay();
            RaiseCommit();
        }
    }
}
