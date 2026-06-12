using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A handle/asset reference rendered as a picker: a dropdown of the project's assets (from the
/// <see cref="AssetCatalog"/>, optionally filtered by the node's <c>$choices</c> type list) that
/// commits the chosen asset's id back to the backing token. Used for [[editor::view("handle"/"asset")]].
/// </summary>
public sealed partial class HandlePickerPropertyViewModel : PropertyViewModelBase
{
    private readonly AssetCatalog? _catalog;
    private readonly IReadOnlyList<string>? _typeFilter;
    private JToken? _token;
    private bool _suppressCommit;

    public HandlePickerPropertyViewModel(PropertyNode node, Action? commit, AssetCatalog? catalog) : base(node)
    {
        _catalog = catalog;
        _typeFilter = node.Choices;
        _token = node.Value;
        CommitChanges = commit;

        Repopulate();

        if (catalog is not null)
            catalog.CatalogUpdated += OnCatalogUpdated;
    }

    public ObservableCollection<AssetEntry> Options { get; } = [];

    [ObservableProperty]
    private AssetEntry? _selected;

    /// <summary>
    /// The raw id when no matching asset is in the catalog (e.g. an unloaded reference), shown so the
    /// reference is never silently blanked.
    /// </summary>
    public long CurrentId => _token?.Value<long?>() ?? 0;

    private void OnCatalogUpdated() => Dispatch.To(DispatchContext.UI, Repopulate);

    private void Repopulate()
    {
        Options.Clear();
        if (_catalog is not null)
        {
            foreach (var asset in _catalog.AssetsOfType(_typeFilter))
                Options.Add(asset);
        }

        // Reselect without re-committing: matching the current token id to an option.
        _suppressCommit = true;
        Selected = Options.FirstOrDefault(asset => asset.Id == CurrentId);
        _suppressCommit = false;
    }

    partial void OnSelectedChanged(AssetEntry? value)
    {
        if (_suppressCommit || value is null || _token is null)
            return;

        // Replace and re-hold the token so repeated picks keep persisting (a detached token wouldn't).
        var replacement = new JValue(value.Id);
        _token.Replace(replacement);
        _token = replacement;
        RaiseCommit();
    }
}
