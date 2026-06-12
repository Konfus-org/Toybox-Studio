using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A script (or asset) reference shown read-only as a hyperlink of its resolved name, from the
/// <see cref="AssetCatalog"/>. Used for [[editor::view("script")]]. Clicking activates the reference
/// (raising <see cref="AssetCatalog.AssetActivated"/>); the raw id is shown until the catalog loads.
/// </summary>
public sealed partial class ScriptLinkPropertyViewModel : PropertyViewModelBase
{
    private readonly AssetCatalog? _catalog;
    private readonly long _id;

    public ScriptLinkPropertyViewModel(PropertyNode node, AssetCatalog? catalog) : base(node)
    {
        _catalog = catalog;
        _id = node.Value?.Value<long?>() ?? 0;
        _displayName = ResolveDisplayName();

        if (catalog is not null)
            catalog.CatalogUpdated += OnCatalogUpdated;
    }

    [ObservableProperty]
    private string _displayName;

    public bool HasReference => _id != 0;

    private void OnCatalogUpdated() => Dispatch.To(DispatchContext.UI, () => DisplayName = ResolveDisplayName());

    private string ResolveDisplayName()
    {
        if (_id == 0)
            return "(none)";

        return _catalog?.ResolveName(_id) ?? $"#{_id}";
    }

    [RelayCommand]
    private void Open()
    {
        if (_id != 0)
            _catalog?.Activate(_id);
    }
}
