using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// Loads the typed <see cref="Asset"/> mirror for a catalog row. It resolves the row's extension token
/// to its payload class through <see cref="AssetKinds"/> and constructs it by id — and constructing a
/// mirror with a real id IS the load (it binds and hydrates itself; await <see cref="Asset.Loaded"/> to
/// observe it). Returns null when no asset class claims the type.
/// </summary>
public static class AssetLoader
{
    public static Asset? Load(AssetEntry entry) =>
        AssetKinds.ForExtension(entry.Type) is { } type
            ? (Asset)Activator.CreateInstance(type, entry.Id)!
            : null;
}
