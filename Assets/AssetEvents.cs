namespace Toybox.Studio.Assets;

// Every event struct the asset domain dispatches, in one place. Handlers implement IEventHandler<T>
// and register with the shared EventDispatcher; nobody references the publisher. Events fire on
// whatever thread raised them — handlers marshal themselves.

/// <summary>The <see cref="AssetCatalog"/> swapped in a new listing (a refresh, or the emptying when
/// the engine disconnects), carrying the snapshot it swapped to.</summary>
public readonly record struct AssetCatalogChanged(IReadOnlyList<AssetEntry> Entries);
