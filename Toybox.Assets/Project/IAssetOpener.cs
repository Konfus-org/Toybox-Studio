namespace Toybox.Studio.Project;

/// <summary>
/// Opens an asset on the editor surface that fits its type (script editor, world, asset viewer, or the OS
/// default program). The concrete opener lives in the app layer since it drives those UIs; the asset services
/// hold this interface so an asset handle's <c>Open()</c> doesn't depend on the UI layer.
/// </summary>
public interface IAssetOpener
{
    Task OpenAsync(AssetMeta asset, bool newWindow = false);
}
