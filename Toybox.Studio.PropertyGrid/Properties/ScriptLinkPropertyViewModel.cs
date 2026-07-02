using Toybox.Studio.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// A script (or asset) reference shown read-only as a hyperlink of its resolved name, from the
/// <see cref="AssetCatalog"/>. Used for [[editor::view("script")]]. Clicking activates the reference
/// (raising <see cref="AssetCatalog.AssetActivated"/>); the raw id is shown until the catalog loads.
/// </summary>
public sealed partial class ScriptLinkPropertyViewModel : PropertyViewModel
{
    private readonly AssetCatalog? _catalog;
    private readonly ulong _id;

    [ObservableProperty]
    private string _displayName;

    public ScriptLinkPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor, AssetCatalog? catalog)
        : base(descriptor, accessor)
    {
        _catalog = catalog;
        _id = ReadId(accessor);
        _displayName = ResolveDisplayName();

        if (catalog is not null)
            catalog.Changed += OnCatalogUpdated;
    }

    public bool HasReference => _id != 0;

    private void OnCatalogUpdated() => Dispatch.To(DispatchContext.UI, () => DisplayName = ResolveDisplayName());

    private string ResolveDisplayName()
    {
        if (_id == 0)
            return "(none)";

        return _catalog?.ResolveName(_id) ?? $"#{_id}";
    }

    // The referenced id, however the value arrives: an AssetHandle (handle wire), a bare ulong (uuid), or a
    // bare integer token (untyped) — read the live accessor value and reduce it to the id.
    private static ulong ReadId(IValueAccessor accessor) => accessor.Get() switch
    {
        AssetHandle handle => handle.Id,
        ulong id => id,
        long id => (ulong)id,
        int id => (ulong)id,
        { } other => EngineSyncValue.WriteBare(other).Value<ulong?>() ?? 0,
        _ => 0,
    };

    [RelayCommand]
    private void Open()
    {
        if (_id != 0)
            _catalog?.Activate(_id);
    }
}
