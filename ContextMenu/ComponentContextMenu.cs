using System.Threading.Tasks;
using System.Threading;
using Toybox.Studio.Clipboards;
using Toybox.Studio.Dialogs;
using Toybox.Studio.Favorites;
using Toybox.Studio.Utils;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The inspector component-header menu: copy / paste / remove the right-clicked component. Routed for the
/// clicked <see cref="ComponentViewModel"/>, it acts on that component on the selected entity. Knows only the
/// world domain.
/// </summary>
public sealed class ComponentContextMenu : ContextMenu<ComponentViewModel>
{
    private readonly GameState _game;
    private readonly WorldSelection _selection;
    private readonly Clipboard _clipboard;

    public ComponentContextMenu(
        FavoritesManager favorites, GameState game, WorldSelection selection, Clipboard clipboard)
        : base(favorites)
    {
        _game = game;
        _selection = selection;
        _clipboard = clipboard;
    }

    protected override async Task Build(MenuBuilder menu, ComponentViewModel target)
    {
        if (_selection.PrimaryId is not { } entityId || string.IsNullOrEmpty(target.Name))
            return;

        var component = target.Name;
        var canPaste = await _clipboard.Has<Component>().ContinueOnAnyContext();

        menu.Item("Copy Component", Icon.Copy)
            .Run(() => CopyAsync(entityId, component));
        menu.Item("Paste Component", Icon.ClipboardPaste)
            .VisibleWhen(canPaste).Run(() => PasteAsync(entityId));
        menu.Separator();
        menu.Item("Remove Component", Icon.Trash2).Color(Utils.Colors.Red)
            .Run("Couldn't remove component", () => RemoveAsync(entityId, component));
    }

    // Copy reads the live component's serialized body (the uniform Serialize every reflected object shares).
    private async Task CopyAsync(ulong entityId, string component)
    {
        if (_game.Active.Find(entityId)?.Components.FirstOrDefault(candidate => candidate.Name == component)
            is { } instance)
            await _clipboard.CopyObject<Component>(instance, component).ContinueOnAnyContext();
    }

    private async Task PasteAsync(ulong entityId)
    {
        if (await _clipboard.PasteObject<Component>().ContinueOnAnyContext()
            is not ({ } value, { Length: > 0 } component))
            return;

        var world = _game.Active;
        var result = await world.GetEntity(entityId).GetComponent(component)
            .Deserialize(value, CancellationToken.None).ContinueOnAnyContext();
        if (!result.Success)
            await Popups.ShowErrorAsync("Couldn't paste component", result.Error!).ContinueOnAnyContext();

        await world.RefreshAsync().ContinueOnAnyContext();
    }

    private async Task<Result> RemoveAsync(ulong entityId, string component)
    {
        var world = _game.Active;
        var result = await world.GetEntity(entityId).GetComponent(component)
            .RemoveAsync(CancellationToken.None).ContinueOnAnyContext();
        await world.RefreshAsync().ContinueOnAnyContext();
        return result;
    }
}
