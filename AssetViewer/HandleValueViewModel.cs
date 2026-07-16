using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;
using Toybox.Studio.PropertyGrid.Slots;
using Toybox.Studio.PropertyGrid;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Composition;

namespace Toybox.Studio.AssetViewer;

/// <summary>
/// Edits an asset-reference <see cref="Handle"/> (a material's base, a texture binding, a model): a
/// name link that opens the referenced asset in the Asset Viewer, a pick button that chooses one from
/// the catalog (filtered to the property's <c>[AssetType]</c> tokens), and a clear button. The value is
/// the accessor's <see cref="Handle"/>; picking writes the chosen asset's handle back. This is what
/// makes a material/model reference in the reflection grid resolvable and re-openable — the migration's
/// "open the asset into the inspector" seam.
/// </summary>
public sealed partial class HandleValueViewModel : ValueViewModel
{
    private readonly AssetCatalog _catalog;
    private readonly AssetOpener _opener;
    private readonly Popups _popups;
    private readonly ViewModelFactory _viewModels;
    private readonly IReadOnlyList<string>? _filter;

    public HandleValueViewModel(
        PropertyValueAccessor accessor, AssetCatalog catalog, AssetOpener opener, Popups popups,
        ViewModelFactory viewModels, IReadOnlyList<string>? filter = null)
        : base(accessor)
    {
        _catalog = catalog;
        _opener = opener;
        _popups = popups;
        _viewModels = viewModels;
        _filter = filter;
        accessor.Changed += Refresh;
    }

    /// <summary>Whether the handle references anything (drives the open/clear affordances).</summary>
    public bool HasValue => Current.IsValid;

    /// <summary>Whether the reference resolves to a known catalog asset the viewer can open.</summary>
    public bool CanOpen => Resolve() is not null;

    /// <summary>The referenced asset's name, its raw label, or its id — or "(none)".</summary>
    public string DisplayName
    {
        get
        {
            var handle = Current;
            if (!handle.IsValid)
                return "(none)";
            if (Resolve() is { } entry)
                return entry.Name;
            return handle.Name.Length > 0 ? handle.Name : $"#{handle.Id:x}";
        }
    }

    private Handle Current => Accessor.Get() as Handle? ?? Handle.None;

    private AssetEntry? Resolve() => Current.IsValid ? _catalog.Find(Current.Id) : null;

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

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open()
    {
        if (Resolve() is { } entry)
            _opener.Open(entry);
    }

    [RelayCommand(CanExecute = nameof(HasValue))]
    private void Clear()
    {
        if (!IsReadOnly)
            Accessor.Set(Handle.None);
    }

    private IEnumerable<AssetEntry> Candidates()
    {
        var entries = _catalog.Entries.Where(entry => entry.Type != "world");
        if (_filter is { Count: > 0 })
        {
            entries = entries.Where(
                entry => _filter.Any(ext => string.Equals(ext, entry.Type, StringComparison.OrdinalIgnoreCase)));
        }

        return entries.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(HasValue));
        OnPropertyChanged(nameof(CanOpen));
        OnPropertyChanged(nameof(DisplayName));
        OpenCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }
}
