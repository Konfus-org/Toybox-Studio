using System.Threading.Tasks;
using Toybox.Studio.Favorites;
using Toybox.Studio.Project;
using Toybox.Studio.Utils;
using Toybox.Studio.AssetBrowser;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The asset-tile menu: open (reusing the window or in a new one), rename, duplicate, copy/paste, delete. Routed
/// for the clicked <see cref="AssetTileViewModel"/>, it acts on it through the static <see cref="Asset"/> API; it
/// knows only the asset domain.
/// </summary>
public sealed class AssetContextMenu : ContextMenu<AssetTileViewModel>
{
    private readonly AssetFactory _factory;

    public AssetContextMenu(FavoritesManager favorites, AssetFactory factory) : base(favorites) =>
        _factory = factory;

    protected override async Task Build(MenuBuilder menu, AssetTileViewModel target)
    {
        var info = target.Asset;
        var canPaste = await _factory.CanPasteAsync().ContinueOnAnyContext();

        menu.Item("Open", Icon.FolderOpen)
            .Run(() => target.OpenCommand.Execute(null));
        menu.Item("Open in New Window", Icon.ExternalLink)
            .Run(() => target.OpenInNewWindowCommand.Execute(null));
        menu.Separator();
        menu.Item("Rename", Icon.Pencil).Gesture("F2")
            .Run(() => _factory.For(info).RenameAsync());
        menu.Item("Duplicate", Icon.CopyPlus).Gesture("Ctrl+D")
            .Run(() => _factory.For(info).DuplicateAsync());
        menu.Item("Copy", Icon.Copy).Gesture("Ctrl+C")
            .Run(() => _factory.For(info).CopyAsync());
        menu.Item("Paste", Icon.ClipboardPaste).Gesture("Ctrl+V")
            .VisibleWhen(canPaste).Run(() => _factory.PasteAsync(_factory.For(info)));
        menu.Separator();
        menu.Item("Delete", Icon.Trash2).Color(Utils.Colors.Red).Gesture("Del")
            .Run(() => _factory.For(info).DeleteAsync());
    }
}
