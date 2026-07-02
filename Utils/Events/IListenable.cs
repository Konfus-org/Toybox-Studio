namespace Toybox.Studio.Utils;

/// <summary>
/// A data holder that notifies listeners when its contents change, so any area of the editor can react to a
/// settings (or other asset) update without polling. Implemented by <c>SettingsManager</c> (editor settings) and
/// <c>AssetCatalog</c> (the engine's assets/scripts). Subscribe with <see cref="ListenableExtensions.Listen"/>
/// for the common "react now and on every change" pattern.
/// </summary>
public interface IListenable
{
    /// <summary>Raised after the holder's contents change. Listeners marshal to the UI thread as needed.</summary>
    event Action? Changed;
}
