using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Toybox.Studio.Services.Clipboard;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.World;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.Commands;

/// <summary>
/// The editor-side actions a context menu invokes — the ones the engine can't do directly because they
/// coordinate the selection, the clipboard, or the world's dirty/refresh cycle (delete, move, duplicate,
/// rename, enable/disable, make global, add, and cut/copy/paste of entities and components). The code-driven
/// menus in <see cref="Widgets.ContextMenu.ContextMenuService"/> bind each row straight to one of these
/// methods, and gate a row's visibility on the matching predicate (e.g. <see cref="CanMoveUp"/>,
/// <see cref="SelectionHasGlobal"/>). Entity actions operate on the current <see cref="WorldSelection"/> (the
/// menu-open gesture selects first); component actions use the supplied <see cref="MenuContext"/>.
/// </summary>
public sealed class EditorCommands
{
    private readonly WorldManager _world;
    private readonly WorldSelection _selection;
    private readonly Clipboard.Clipboard _clipboard;
    private readonly Logger _log;

    public EditorCommands(
        WorldManager world, WorldSelection selection, Clipboard.Clipboard clipboard, Logger log)
    {
        _world = world;
        _selection = selection;
        _clipboard = clipboard;
        _log = log;
    }

    /// <summary>Raised when a "Rename" action asks the world view to drop the entity into inline rename (a
    /// view-layer action the world view owns; <see cref="Widgets.Ecs.WorldViewModel"/> subscribes).</summary>
    public event Action<ulong>? RenameRequested;

    // The entities an action targets: the whole selection (the menu-open gesture selects the right-clicked
    // entity first), newest last so the primary is the last entry.
    private IReadOnlyList<ulong> Targets => _selection.SelectedIds;

    public async Task DeleteAsync()
    {
        // Snapshot the ids: destroying mutates the selection as the refresh reconciles it away.
        foreach (var id in Targets.ToList())
        {
            var result = await _world.Entity(id).DestroyAsync(CancellationToken.None).ContinueOnAnyContext();
            if (!result.Success)
            {
                await Popups.ShowErrorAsync("Couldn't delete entity", result.Error!).ContinueOnAnyContext();
                break;
            }
        }

        _world.MarkDirty();
        await _world.RefreshAsync().ContinueOnAnyContext();
    }

    public async Task MoveAsync(bool up)
    {
        if (_selection.PrimaryId is not { } id || Find(id) is not { } located)
            return;

        var (entity, parentId) = located;
        var siblings = SiblingsOf(parentId, entity.IsGlobal);
        var current = siblings.FindIndex(sibling => sibling.Id == id);
        var destination = up ? current - 1 : current + 1;
        if (current < 0 || destination < 0 || destination >= siblings.Count)
            return; // Already at the end it's heading toward.

        var result = await _world.Entity(id).MoveAsync(parentId, destination, CancellationToken.None)
            .ContinueOnAnyContext();
        if (result.Success)
            _world.MarkDirty();
        else
            await Popups.ShowErrorAsync("Couldn't move entity", result.Error!).ContinueOnAnyContext();

        await _world.RefreshAsync().ContinueOnAnyContext();
    }

    public async Task DuplicateAsync()
    {
        foreach (var id in Targets.ToList())
        {
            var parentId = Find(id)?.parentId ?? 0UL;
            if (await _world.Entity(id).SerializeAsync().ContinueOnAnyContext() is { Success: true, Value: { } body })
                await _world.Active.AddEntityFromJsonAsync(body, parentId).ContinueOnAnyContext();
        }

        _world.MarkDirty();
        await _world.RefreshAsync().ContinueOnAnyContext();
    }

    public async Task AddAsync(bool global)
    {
        var created = await _world.CreateEntityAsync("Entity", parent: 0UL, CancellationToken.None)
            .ContinueOnAnyContext();
        if (created is not { Success: true, Value: { } entity })
        {
            await Popups.ShowErrorAsync("Couldn't add entity", created.Error!).ContinueOnAnyContext();
            return;
        }

        if (global)
            await entity.SetGlobalAsync(true, CancellationToken.None).ContinueOnAnyContext();

        _world.MarkDirty();
        await _world.RefreshAsync().ContinueOnAnyContext();
        _selection.Select(entity.Id);
    }

    public void Rename()
    {
        if (_selection.PrimaryId is { } id)
            RenameRequested?.Invoke(id);
    }

    public async Task ToggleEnabledAsync()
    {
        if (_selection.PrimaryId is not { } id || Find(id) is not { } located)
            return;

        var result = await _world.Entity(id)
            .SetEnabledAsync(!located.entity.IsEnabled, CancellationToken.None).ContinueOnAnyContext();
        if (result.Success)
            _world.MarkDirty();
        else
            await Popups.ShowErrorAsync("Couldn't change entity state", result.Error!).ContinueOnAnyContext();

        await _world.RefreshAsync().ContinueOnAnyContext();
    }

    public async Task SetGlobalAsync(bool global)
    {
        foreach (var id in Targets.ToList())
        {
            var result = await _world.Entity(id).SetGlobalAsync(global, CancellationToken.None)
                .ContinueOnAnyContext();
            if (!result.Success)
            {
                await Popups.ShowErrorAsync("Couldn't change global state", result.Error!)
                    .ContinueOnAnyContext();
                break;
            }
        }

        _world.MarkDirty();
        await _world.RefreshAsync().ContinueOnAnyContext();
    }

    public async Task CopyEntityAsync()
    {
        if (_selection.PrimaryId is not { } id)
            return;

        if (await _world.Entity(id).SerializeAsync().ContinueOnAnyContext() is { Success: true, Value: { } json })
            await _clipboard.Copy(new ClipboardEntity { Body = json }).ContinueOnAnyContext();
    }

    public async Task CutEntityAsync()
    {
        await CopyEntityAsync().ContinueOnAnyContext();
        await DeleteAsync().ContinueOnAnyContext();
    }

    public async Task PasteEntityAsync()
    {
        // Paste keeps the clipboard (a non-consuming read) so the same entity can be pasted repeatedly.
        if (await _clipboard.Paste<ClipboardEntity>().ContinueOnAnyContext() is not { Body: { } json })
            return;

        var spawned = await _world.Active.AddEntityFromJsonAsync(json, parent: 0UL).ContinueOnAnyContext();
        _world.MarkDirty();
        await _world.RefreshAsync().ContinueOnAnyContext();
        if (spawned is { Success: true, Value: { } entity })
            _selection.Select(entity.Id);
    }

    public async Task CopyComponentAsync(MenuContext? context)
    {
        if (ComponentTarget(context) is not (var entityId, var component))
            return;

        if (await _world.Entity(entityId).Component(component).ReadAsync().ContinueOnAnyContext()
            is { Success: true, Value: { } body })
            await _clipboard
                .Copy(new ClipboardComponent { Component = component, Value = body })
                .ContinueOnAnyContext();
    }

    public async Task PasteComponentAsync(MenuContext? context)
    {
        var entityId = context?.EntityId ?? _selection.PrimaryId;
        if (entityId is not { } id
            || await _clipboard.Paste<ClipboardComponent>().ContinueOnAnyContext()
                is not { Component: { Length: > 0 } component, Value: { } value })
            return;

        var result = await _world.Entity(id).Component(component).SetAsync(value, CancellationToken.None)
            .ContinueOnAnyContext();
        if (!result.Success)
            await Popups.ShowErrorAsync("Couldn't paste component", result.Error!).ContinueOnAnyContext();

        _world.MarkDirty();
        await _world.RefreshAsync().ContinueOnAnyContext();
    }

    public async Task RemoveComponentAsync(MenuContext? context)
    {
        if (ComponentTarget(context) is not (var entityId, var component))
            return;

        var result = await _world.Entity(entityId).Component(component)
            .RemoveAsync(CancellationToken.None).ContinueOnAnyContext();
        if (result.Success)
            _world.MarkDirty();
        else
            await Popups.ShowErrorAsync("Couldn't remove component", result.Error!).ContinueOnAnyContext();

        await _world.RefreshAsync().ContinueOnAnyContext();
    }

    /// <summary>Whether the single-selected entity can move up within its sibling bucket (not already first).</summary>
    public bool CanMoveUp(ulong id) => MoveIndex(id) is { } where && where.index > 0;

    /// <summary>Whether the single-selected entity can move down within its sibling bucket (not already last).</summary>
    public bool CanMoveDown(ulong id) => MoveIndex(id) is { } where && where.index < where.count - 1;

    /// <summary>True when any selected entity is streamed (not global) — so "Make Global" is worth offering.</summary>
    public bool SelectionHasStreamed() => Targets.Any(id => Find(id) is { entity.IsGlobal: false });

    /// <summary>True when any selected entity is global — so "Make Streamed" is worth offering.</summary>
    public bool SelectionHasGlobal() => Targets.Any(id => Find(id) is { entity.IsGlobal: true });

    // The (display index, sibling count) of an entity within its bucket, or null when it can't be located.
    private (int index, int count)? MoveIndex(ulong id)
    {
        if (Find(id) is not { } located)
            return null;

        var siblings = SiblingsOf(located.parentId, located.entity.IsGlobal);
        var index = siblings.FindIndex(sibling => sibling.Id == id);
        return index < 0 ? null : (index, siblings.Count);
    }

    // The (entity, component) a component action targets, or null when nothing is addressable.
    private (ulong entityId, string component)? ComponentTarget(MenuContext? context)
    {
        var entityId = context?.EntityId ?? _selection.PrimaryId;
        return entityId is { } id && !string.IsNullOrEmpty(context?.Component)
            ? (id, context!.Component!)
            : null;
    }

    // Locates an entity in the current world snapshot, returning it with its parent's id (0 at the root).
    private (EntityDescription entity, ulong parentId)? Find(ulong id)
    {
        (EntityDescription, ulong)? Walk(IReadOnlyList<EntityDescription> nodes, ulong parentId)
        {
            foreach (var node in nodes)
            {
                if (node.Id == id)
                    return (node, parentId);
                if (Walk(node.Children, node.Id) is { } found)
                    return found;
            }

            return null;
        }

        return Walk(_world.Current.Roots, 0UL);
    }

    // The display siblings of an entity: same parent and same bucket (streamed vs global), in display order.
    private List<EntityDescription> SiblingsOf(ulong parentId, bool isGlobal)
    {
        var children = parentId == 0
            ? _world.Current.Roots
            : Find(parentId)?.entity.Children as IReadOnlyList<EntityDescription> ?? [];
        return children
            .Where(child => child.IsGlobal == isGlobal)
            .OrderBy(child => child.Order)
            .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
