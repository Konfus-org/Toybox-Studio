namespace Toybox.Studio.Project;

/// <summary>
/// Holds the asset services the property-grid's asset editors (handle / material pickers) and the entity
/// inspector read while building grids. Lives in the asset layer rather than the grid core so the generic
/// property grid stays free of asset-system references; wired once at startup (see App startup).
/// </summary>
public static class AssetGridServices
{
    /// <summary>The asset catalog the handle/asset pickers resolve against.</summary>
    public static AssetCatalog? Assets { get; private set; }

    /// <summary>The asset factory the material-instance override editor uses to load a base material's body.</summary>
    public static AssetFactory? Factory { get; private set; }

    /// <summary>Supplies the services. Called once after the app's services are built.</summary>
    public static void Configure(AssetCatalog assets, AssetFactory factory)
    {
        Assets = assets;
        Factory = factory;
    }
}
