using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Toybox.Studio.Clipboards;
using Toybox.Studio.Dialogs;
using Toybox.Studio.Favorites;
using Toybox.Studio.Worlds;
using Toybox.Studio.Utils;
using Toybox.Studio.Ecs;
using Toybox.Studio.WorldTree;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The one world context menu, shown over any world surface — a tree row, a viewport billboard, or the empty
/// space of either. Routed for any <see cref="IWorldMenuTarget"/>: when an entity was clicked
/// (<see cref="IWorldMenuTarget.EntityId"/>) it shows the entity verbs (rename, cut/copy/paste, duplicate, move
/// up/down, make global/streamed, enable/disable, delete); over empty space it shows add / paste-entity. The
/// tree-only verbs (rename, reorder) are view-layer actions the tree owns, so they hide in the viewport via
/// <see cref="IWorldMenuTarget.Surface"/>. It acts on the current <see cref="WorldSelection"/> — selecting the
/// right-clicked entity first — calling the runtime-like behaviours on the <see cref="Entity"/>/<see cref="World"/>
/// handles directly. Knows only the world domain.
/// </summary>
public sealed class WorldContextMenu : ContextMenu<IWorldMenuTarget>
{
    private readonly WorldSelection _selection;
    private readonly GameState _game;
    private readonly Clipboard _clipboard;

    public WorldContextMenu(
        FavoritesManager favorites, WorldSelection selection, GameState game, Clipboard clipboard)
        : base(favorites)
    {
        _selection = selection;
        _game = game;
        _clipboard = clipboard;
    }

    /// <summary>Raised when "Rename" is chosen, so the world tree drops the entity into inline rename (a view-
    /// layer action the tree owns). The world view subscribes.</summary>
    public event Action<ulong>? RenameRequested;

    /// <summary>Raised when "Move Up" (delta -1) / "Move Down" (delta +1) is chosen. The reorder itself is
    /// view-model logic (computing siblings + setting the entity's Order), so the world view performs it.</summary>
    public event Action<ulong, int>? MoveRequested;

    protected override Task Build(MenuBuilder menu, IWorldMenuTarget target) =>
        target.EntityId is { } clicked
            ? BuildEntityMenu(menu, clicked, target.Surface)
            : BuildBackgroundMenu(menu);

    // Entity right-click: rename, clipboard, duplicate, reorder, global/streamed, enable/disable, delete. Rename
    // and Move Up/Down are tree-only view actions, so they stay hidden when the click came from the viewport.
    private async Task BuildEntityMenu(MenuBuilder menu, ulong clicked, WorldMenuSurface surface)
    {
        // Right-clicking targets what was clicked: select it first, unless it's already in a multi-selection.
        if (!_selection.Contains(clicked))
            _selection.Select(clicked);

        if (_selection.PrimaryId is not { } primaryId)
            return;

        var world = _game.Active;
        var entities = _selection.SelectedIds.Select(world.Find).OfType<Entity>().ToList();
        var primary = world.Find(primaryId);
        var single = _selection.SelectedIds.Count == 1;
        var inTree = surface == WorldMenuSurface.Tree;
        var canPaste = await _clipboard.Has<Entity>().ContinueOnAnyContext();

        menu.Item("Rename", Icon.Pencil).Gesture("F2")
            .VisibleWhen(inTree && single)
            .Run(() => RenameRequested?.Invoke(primaryId));

        menu.Separator();
        menu.Item("Cut", Icon.Scissors).Gesture("Ctrl+X").Run(() => CutAsync(entities));
        menu.Item("Copy", Icon.Copy).Gesture("Ctrl+C").Run(() => CopyAsync(primary));
        menu.Item("Paste", Icon.ClipboardPaste).Gesture("Ctrl+V")
            .VisibleWhen(canPaste).Run(PasteAsync);
        menu.Item("Duplicate", Icon.CopyPlus).Gesture("Ctrl+D")
            .Run("Couldn't duplicate entity", () => world.DuplicateAsync(entities));

        menu.Separator();
        menu.Item("Move Up", Icon.ArrowUp)
            .VisibleWhen(inTree && single && CanShift(primary, -1))
            .Run(() => MoveRequested?.Invoke(primaryId, -1));
        menu.Item("Move Down", Icon.ArrowDown)
            .VisibleWhen(inTree && single && CanShift(primary, +1))
            .Run(() => MoveRequested?.Invoke(primaryId, +1));

        menu.Separator();
        menu.Item("Make Global", Icon.Globe)
            .VisibleWhen(entities.Any(entity => !entity.IsGlobal))
            .Run("Couldn't change global state", () => world.SetGlobalAsync(entities, true));
        menu.Item("Make Streamed", Icon.Layers)
            .VisibleWhen(entities.Any(entity => entity.IsGlobal))
            .Run("Couldn't change global state", () => world.SetGlobalAsync(entities, false));
        menu.Item("Enable / Disable", Icon.Power)
            .VisibleWhen(single && primary is not null)
            .Run(() => ToggleEnabledAsync(primary!));

        menu.Separator();
        menu.Item("Delete", Icon.Trash2).Color(Utils.Colors.Red).Gesture("Del")
            .Run("Couldn't delete entity", () => world.DeleteAsync(entities));
    }

    // Empty-space right-click: add a streamed / global entity, or paste one when the clipboard holds one.
    private async Task BuildBackgroundMenu(MenuBuilder menu)
    {
        var canPaste = await _clipboard.Has<Entity>().ContinueOnAnyContext();

        menu.Item("Add Entity", Icon.Plus).Color(Utils.Colors.Green)
            .Run(() => AddAsync(global: false));
        menu.Item("Add Global Entity", Icon.Globe).Color(Utils.Colors.Green)
            .Run(() => AddAsync(global: true));

        menu.Separator();
        menu.Item("Paste", Icon.ClipboardPaste).Gesture("Ctrl+V")
            .VisibleWhen(canPaste).Run(PasteAsync);
    }

    // Whether the entity can shift by delta among its same-bucket siblings (used only for menu visibility; the
    // reorder itself lives in the world view-model). Null primary can't move.
    private static bool CanShift(Entity? entity, int delta)
    {
        if (entity is null)
            return false;
        var siblings = WorldViewModel.SiblingsOf(entity);
        var target = siblings.FindIndex(sibling => sibling.Id == entity.Id) + delta;
        return target >= 0 && target < siblings.Count;
    }

    private async Task AddAsync(bool global)
    {
        var world = _game.Active;
        var added = await world.AddEntityAsync(global).ContinueOnAnyContext();
        if (!added.Success)
        {
            await Popups.ShowErrorAsync("Couldn't add entity", added.Error!).ContinueOnAnyContext();
            return;
        }

        await world.RefreshAsync().ContinueOnAnyContext();
        if (added.Value is { } entity)
            _selection.Select(entity.Id);
    }

    // Copy keeps the clipboard (a non-consuming read) so the same entity can be pasted repeatedly. Copy/paste is
    // the uniform Serialize/Deserialize every reflected object shares — it's all just JSON.
    private async Task CopyAsync(Entity? entity)
    {
        if (entity is not null)
            await _clipboard.CopyObject<Entity>(entity).ContinueOnAnyContext();
    }

    private async Task CutAsync(IReadOnlyList<Entity> entities)
    {
        // The primary (the last-selected) is what a single Copy would take.
        if (entities.Count > 0)
            await CopyAsync(entities[^1]).ContinueOnAnyContext();
        var result = await _game.Active.DeleteAsync(entities).ContinueOnAnyContext();
        if (!result.Success)
            await Popups.ShowErrorAsync("Couldn't delete entity", result.Error!).ContinueOnAnyContext();
    }

    private async Task PasteAsync()
    {
        if (await _clipboard.PasteObject<Entity>().ContinueOnAnyContext() is not ({ } json, _))
            return;

        // Copy/paste is the uniform Serialize/Deserialize: spawn a fresh entity, then deserialize the body into it.
        var world = _game.Active;
        var created = await world.CreateEntityAsync("Entity", parent: 0UL, CancellationToken.None)
            .ContinueOnAnyContext();
        if (created is not { Success: true, Value: { } entity })
        {
            await Popups.ShowErrorAsync("Couldn't paste entity", created.Error!).ContinueOnAnyContext();
            return;
        }

        var restored = await entity.Deserialize(json, CancellationToken.None).ContinueOnAnyContext();
        if (!restored.Success)
            await Popups.ShowErrorAsync("Couldn't paste entity", restored.Error!).ContinueOnAnyContext();

        await world.RefreshAsync().ContinueOnAnyContext();
        _selection.Select(entity.Id);
    }

    private async Task ToggleEnabledAsync(Entity entity)
    {
        var world = _game.Active;
        var result = await entity.SetEnabledAsync(!entity.IsEnabled, CancellationToken.None).ContinueOnAnyContext();
        if (!result.Success)
            await Popups.ShowErrorAsync("Couldn't change entity state", result.Error!).ContinueOnAnyContext();

        await world.RefreshAsync().ContinueOnAnyContext();
    }
}
