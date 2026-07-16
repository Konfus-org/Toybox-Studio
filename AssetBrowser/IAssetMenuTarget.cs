namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// Something the one Asset Browser context menu can open over — a clicked tile or the empty grid space. Routing
/// is by type, so every browser surface implements this and the menu is declared once for
/// <c>IAssetMenuTarget</c>. <see cref="Browser"/> is always the owning panel (the create rows author through its
/// commands, so a right-click <i>anywhere</i> can make an asset); <see cref="Tile"/> is the right-clicked tile,
/// or null over empty space (so the menu adds the per-tile verbs only when a tile was clicked).
/// </summary>
public interface IAssetMenuTarget
{
    /// <summary>The owning Asset Browser panel — the create rows run through its authoring commands.</summary>
    AssetBrowserViewModel Browser { get; }

    /// <summary>The right-clicked tile, or null when the empty grid space was clicked.</summary>
    AssetTileViewModel? Tile { get; }
}
