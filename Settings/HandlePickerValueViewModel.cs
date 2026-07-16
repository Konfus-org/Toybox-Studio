using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.PropertyGrid.Slots;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio.Settings;

/// <summary>
/// Edits an asset-reference <see cref="Handle"/> in the Project settings grid (the app <c>Icon</c>, the
/// <c>Startup World</c>): a name button that opens a catalog picker plus a clear button. It's a
/// lightweight editor of its own rather than the Asset Viewer's handle picker because the Settings
/// project sits below the viewer (which references it) and a settings reference doesn't open into the
/// viewer. The property's optional <c>[AssetType]</c> tokens filter the picker; a <c>"world"</c> filter
/// is what lets the Startup World list worlds, which the general picker deliberately hides.
/// </summary>
public sealed partial class HandlePickerValueViewModel : ValueViewModel
{
    private readonly AssetCatalog _catalog;
    private readonly Popups _popups;
    private readonly ViewModelFactory _viewModels;
    private readonly IReadOnlyList<string>? _filter;

    public HandlePickerValueViewModel(
        PropertyValueAccessor accessor, AssetCatalog catalog, Popups popups, ViewModelFactory viewModels,
        IReadOnlyList<string>? filter = null)
        : base(accessor)
    {
        _catalog = catalog;
        _popups = popups;
        _viewModels = viewModels;
        _filter = filter;
        accessor.Changed += Refresh;
    }

    /// <summary>Whether the handle references anything (drives the clear affordance).</summary>
    public bool HasValue => Current.IsValid;

    /// <summary>The referenced asset's name, its raw label, or its id — or "(none)".</summary>
    public string DisplayName
    {
        get
        {
            var handle = Current;
            if (!handle.IsValid)
                return "(none)";
            if (_catalog.Find(handle.Id) is { } entry)
                return entry.Name;
            return handle.Name.Length > 0 ? handle.Name : $"#{handle.Id:x}";
        }
    }

    private Handle Current => Accessor.Get() as Handle? ?? Handle.None;

    [RelayCommand]
    private async Task PickAsync()
    {
        if (IsReadOnly)
            return;

        var items = Candidates()
            .Select(entry => new ListPickItem(entry.Id.ToString(CultureInfo.InvariantCulture), entry.Name, entry.Path))
            .ToList();

        var picked = await _popups
            .ShowAsync(_viewModels.Create<ListPickPopupViewModel>("Select Asset", items))
            .ContinueOnSameContext();
        if (picked is null)
            return;

        if (_catalog.Find(ulong.Parse(picked.Key, CultureInfo.InvariantCulture)) is { } entry)
            Accessor.Set(entry.Handle);
    }

    [RelayCommand(CanExecute = nameof(HasValue))]
    private void Clear()
    {
        if (!IsReadOnly)
            Accessor.Set(Handle.None);
    }

    private IEnumerable<AssetEntry> Candidates()
    {
        // An explicit [AssetType] filter takes full control of what shows — and is the only way worlds
        // appear (the Startup World tags "world"). With no filter, exclude worlds: a world is opened as a
        // world, not referenced as a plain asset.
        var entries = _filter is { Count: > 0 }
            ? _catalog.Entries.Where(
                entry => _filter.Any(token => string.Equals(token, entry.Type, StringComparison.OrdinalIgnoreCase)))
            : _catalog.Entries.Where(entry => !string.Equals(entry.Type, "world", StringComparison.OrdinalIgnoreCase));

        return entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasValue));
        OnPropertyChanged(nameof(DisplayName));
        ClearCommand.NotifyCanExecuteChanged();
    }
}
