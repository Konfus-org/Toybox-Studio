using System.Threading.Tasks;
using Toybox.Studio.EngineApi.Types.Worlds;
using Toybox.Studio.Events;
using Toybox.Studio.Favorites;
using Toybox.Studio.Keybindings;
using Toybox.Studio.Utils;
using Toybox.Studio.WorldTree;

namespace Toybox.Studio.ContextMenu;

/// <summary>
/// The one world context menu, shown over any world surface — a tree row or the tree's blank space. Routed
/// for any <see cref="IWorldMenuTarget"/>: over an entity it shows the entity verbs (rename, cut/copy/paste,
/// duplicate, make global/streamed, enable/disable, delete); over empty space it shows add / paste. It
/// selects the right-clicked entity first, then runs the shared <see cref="EntityOperations"/> — the same
/// path the keybindings and the Edit menu use — so a menu Copy and a Ctrl+C are identical. Rename is a
/// tree view action, so it publishes the rename action (the focused panel drops the row into inline edit)
/// and hides off the tree.
/// </summary>
public sealed class WorldContextMenu : ContextMenu<IWorldMenuTarget>
{
    private readonly WorldSelection _selection;
    private readonly EntityOperations _ops;
    private readonly ActionRegistry _actions;

    public WorldContextMenu(
        FavoritesManager favorites, EventDispatcher events, WorldSelection selection, EntityOperations ops,
        ActionRegistry actions)
        : base(favorites, events)
    {
        _selection = selection;
        _ops = ops;
        _actions = actions;
    }

    protected override Task Build(MenuBuilder menu, IWorldMenuTarget target) =>
        target.EntityId is { } clicked
            ? BuildEntityMenu(menu, clicked, target.Surface)
            : BuildBackgroundMenu(menu);

    // Entity right-click: rename (tree only), clipboard, duplicate, global/streamed, enable/disable, delete.
    // Right-clicking targets what was clicked — select it first, unless it's already in a multi-selection.
    private async Task BuildEntityMenu(MenuBuilder menu, ulong clicked, WorldMenuSurface surface)
    {
        if (!_selection.Contains(clicked))
            _selection.Select(clicked);

        var single = _selection.SelectedIds.Count == 1;
        var inTree = surface == WorldMenuSurface.Tree;
        var canPaste = await _ops.CanPasteAsync().ContinueOnAnyContext();

        menu.Item("Rename", Icon.Pencil).Gesture("F2")
            .VisibleWhen(inTree && single).Run(() => _actions.Invoke(ActionIds.EntityRename));

        menu.Separator();
        menu.Item("Cut", Icon.Scissors).Gesture("Ctrl+X").Run(() => _ops.CutAsync());
        menu.Item("Copy", Icon.Copy).Gesture("Ctrl+C").Run(() => _ops.CopyAsync());
        menu.Item("Paste", Icon.ClipboardPaste).Gesture("Ctrl+V").VisibleWhen(canPaste).Run(() => _ops.PasteAsync());
        menu.Item("Duplicate", Icon.CopyPlus).Gesture("Ctrl+D").Run(() => _ops.DuplicateAsync());

        menu.Separator();
        menu.Item("Make Global", Icon.Globe).Run(() => _ops.SetGlobalAsync(true));
        menu.Item("Make Streamed", Icon.Layers).Run(() => _ops.SetGlobalAsync(false));
        menu.Item("Enable / Disable", Icon.Power).VisibleWhen(single).Run(() => _ops.ToggleEnabledAsync());

        menu.Separator();
        menu.Item("Delete", Icon.Trash2).Color(Palette.Red).Gesture("Del").Run(() => _ops.DeleteAsync());
    }

    // Empty-space right-click: add a streamed / global entity, or paste one when the clipboard holds one.
    private async Task BuildBackgroundMenu(MenuBuilder menu)
    {
        var canPaste = await _ops.CanPasteAsync().ContinueOnAnyContext();

        menu.Item("Add Entity", Icon.Plus).Color(Palette.Green).Run(() => _ops.AddEntityAsync(global: false));
        menu.Item("Add Global Entity", Icon.Globe).Color(Palette.Green).Run(() => _ops.AddEntityAsync(global: true));

        menu.Separator();
        menu.Item("Paste", Icon.ClipboardPaste).Gesture("Ctrl+V").VisibleWhen(canPaste).Run(() => _ops.PasteAsync());
    }
}
