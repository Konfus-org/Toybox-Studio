using System.Threading.Tasks;
using Toybox.Studio.AssetBrowser;
using Toybox.Studio.Events;
using Toybox.Studio.Favorites;
using Toybox.Studio.Utils;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The one Asset Browser menu, shown over any browser surface — a tile or the empty grid space. Routed for any
/// <see cref="IAssetMenuTarget"/>: it always offers the "New …" authoring rows (so a right-click <i>anywhere</i>,
/// a card included, can create an asset), authored through the browser's own create commands; and it adds the
/// per-tile verbs (open, rename, duplicate, copy, delete) only when a tile was right-clicked
/// (<see cref="IAssetMenuTarget.Tile"/>), run through <see cref="AssetOperations"/>. Built-in engine/bridge
/// assets have no project file, so their editing verbs stay hidden.
/// </summary>
public sealed class AssetContextMenu : ContextMenu<IAssetMenuTarget>
{
    private readonly AssetOperations _assets;

    public AssetContextMenu(FavoritesManager favorites, EventDispatcher events, AssetOperations assets)
        : base(favorites, events) => _assets = assets;

    protected override async Task Build(MenuBuilder menu, IAssetMenuTarget target)
    {
        // A tile was clicked: its verbs sit above the create rows, separated from them.
        if (target.Tile is { } tile)
            BuildTileVerbs(menu, tile);

        // Paste is available over any surface (a copied asset pastes anywhere), so its clipboard state is
        // resolved once here and handed to the create rows.
        var canPaste = await _assets.CanPasteAsync().ContinueOnSameContext();
        BuildCreateRows(menu, target.Browser, canPaste);
    }

    // The clicked tile's verbs: open (reusing the window or in a new one), then the CRUD verbs. Built-in
    // engine/bridge assets have no project file, so only the open verbs show for them.
    private void BuildTileVerbs(MenuBuilder menu, AssetTileViewModel tile)
    {
        var entry = tile.Entry;
        var editable = !entry.IsBuiltin;

        menu.Item("Open", Icon.FolderOpen)
            .Run(() => tile.OpenCommand.Execute(null));
        menu.Item("Open in New Window", Icon.ExternalLink)
            .Run(() => tile.OpenInNewWindowCommand.Execute(null));

        menu.Separator();
        menu.Item("Rename", Icon.Pencil).Gesture("F2")
            .VisibleWhen(editable).Run(() => _assets.RenameAsync(entry));
        menu.Item("Duplicate", Icon.CopyPlus).Gesture("Ctrl+D")
            .VisibleWhen(editable).Run(() => _assets.DuplicateAsync(entry));
        menu.Item("Copy", Icon.Copy).Gesture("Ctrl+C")
            .VisibleWhen(editable).Run(() => _assets.CopyAsync(entry));
        menu.Item("Reveal in Explorer", Icon.FolderSearch)
            .Keywords("reveal show file explorer folder locate")
            .VisibleWhen(editable).Run(() => _assets.RevealAsync(entry));

        menu.Separator();
        menu.Item("Delete", Icon.Trash2).Color(Palette.Red).Gesture("Del")
            .VisibleWhen(editable).Run(() => _assets.DeleteAsync(entry));

        menu.Separator();
    }

    // The "New …" authoring rows plus Paste and a Reload, always present so a right-click anywhere can create
    // or paste an asset. The browser's commands own the authoring (and the open-on-create), so this menu stays
    // a thin surface; Paste runs through AssetOperations (a paste duplicates the copied asset).
    private void BuildCreateRows(MenuBuilder menu, AssetBrowserViewModel browser, bool canPaste)
    {
        menu.Item("New Material", Icon.Palette)
            .Keywords("create new add material").Color(Palette.Yellow)
            .Run(() => browser.CreateMaterialCommand.Execute(null));
        menu.Item("New Input Map", Icon.Gamepad2)
            .Keywords("create new add input map").Color(Palette.Green)
            .Run(() => browser.CreateInputMapCommand.Execute(null));

        menu.Separator();
        menu.Item("Paste", Icon.ClipboardPaste).Gesture("Ctrl+V")
            .Keywords("paste duplicate clipboard").VisibleWhen(canPaste)
            .Run(() => _assets.PasteAsync());
        menu.Item("Reload", Icon.RefreshCw)
            .Keywords("refresh reload assets").Run(() => browser.ReloadCommand.Execute(null));
    }
}
