using System.Linq;
using Toybox.Studio.Services.Project;

namespace Toybox.Studio.Services.World;

/// <summary>
/// The catalog of script assets that can be attached to an entity: the subset of the engine's assets a
/// scripting backend recognises as a script source (<see cref="Asset.IsScript"/>). Derived from — and kept
/// in step with — the <see cref="AssetCatalog"/>, so it needs no separate engine round-trip; the inspector's
/// "Add Script" picker binds to <see cref="Scripts"/>.
/// </summary>
public sealed class ScriptCatalog
{
    private readonly AssetCatalog _assets;

    public ScriptCatalog(AssetCatalog assets)
    {
        _assets = assets;
        _assets.Changed += OnAssetsChanged;
        Scripts = Filter();
    }

    /// <summary>Raised (on the UI thread, with the asset catalog) after the script set is refreshed.</summary>
    public event Action? Changed;

    public IReadOnlyList<AssetInfo> Scripts { get; private set; }

    private void OnAssetsChanged()
    {
        Scripts = Filter();
        Changed?.Invoke();
    }

    private IReadOnlyList<AssetInfo> Filter() => _assets.Assets.Where(asset => asset.IsScript).ToList();
}
