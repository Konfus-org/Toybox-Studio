using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Clipboard;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Services.Logging;
using Toybox.Studio.Services.World;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Services.Commands;

/// <summary>
/// Runs the editor-side verbs a data-driven context-menu (or toolbar) command names with an <c>editor.*</c>
/// method — the ones the engine can't do directly because they coordinate selection, the clipboard, or the
/// world's dirty/refresh cycle (delete, move up/down, duplicate, rename, enable/disable, make global, and
/// cut/copy/paste of entities and components). The toolbar's <c>ToolCommandRunner</c> delegates any
/// <c>editor.*</c> step it doesn't itself own (play/stop/pause) to <see cref="RunAsync"/>, passing the
/// <see cref="MenuContext"/> the menu was opened with. Entity verbs act on the current
/// <see cref="WorldSelection"/> (the menu-open gesture selects first); component verbs use the context.
/// </summary>
public sealed class EditorCommands
{
    private const string ScriptContainerComponent = "script_container";

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

    /// <summary>Raised when a "Rename" command asks the world view to drop the entity into inline rename (a
    /// view-layer action the world view owns; <see cref="Widgets.Ecs.WorldViewModel"/> subscribes).</summary>
    public event Action<ulong>? RenameRequested;

    /// <summary>
    /// Runs one editor verb. Recognises the <c>editor.*</c> methods below and returns success for anything else
    /// so an unknown verb is skipped rather than failing the whole command. Each verb does its own error
    /// surfacing (a popup) and world refresh, so it returns <see cref="Result.Ok"/> even on a handled failure.
    /// </summary>
    public async Task<Result> RunAsync(string method, JObject? @params, MenuContext? context, CancellationToken ct)
    {
        switch (method)
        {
            case "editor.entity.delete":
                await DeleteAsync().ContinueOnAnyContext();
                return Result.Ok();
            case "editor.entity.moveUp":
                await MoveAsync(up: true).ContinueOnAnyContext();
                return Result.Ok();
            case "editor.entity.moveDown":
                await MoveAsync(up: false).ContinueOnAnyContext();
                return Result.Ok();
            case "editor.entity.duplicate":
                await DuplicateAsync().ContinueOnAnyContext();
                return Result.Ok();
            case "editor.entity.rename":
                Rename(context);
                return Result.Ok();
            case "editor.entity.toggleEnabled":
                await ToggleEnabledAsync().ContinueOnAnyContext();
                return Result.Ok();
            case "editor.entity.setGlobal":
                await SetGlobalAsync(@params?["global"]?.Value<bool>() ?? true).ContinueOnAnyContext();
                return Result.Ok();
            case "editor.entity.add":
                await AddAsync(@params?["global"]?.Value<bool>() ?? false).ContinueOnAnyContext();
                return Result.Ok();
            case "editor.clipboard.copy":
                await CopyEntityAsync().ContinueOnAnyContext();
                return Result.Ok();
            case "editor.clipboard.cut":
                await CopyEntityAsync().ContinueOnAnyContext();
                await DeleteAsync().ContinueOnAnyContext();
                return Result.Ok();
            case "editor.clipboard.paste":
                await PasteEntityAsync().ContinueOnAnyContext();
                return Result.Ok();
            case "editor.component.copy":
                await CopyComponentAsync(context).ContinueOnAnyContext();
                return Result.Ok();
            case "editor.component.paste":
                await PasteComponentAsync(context).ContinueOnAnyContext();
                return Result.Ok();
            case "editor.component.remove":
                await RemoveComponentAsync(context).ContinueOnAnyContext();
                return Result.Ok();
            default:
                _log.Warning($"Unknown editor command '{method}'; skipping.");
                return Result.Ok();
        }
    }

    // The entities a command targets: the whole selection (the menu-open gesture selects the right-clicked
    // entity first), newest last so the primary is the last entry.
    private IReadOnlyList<ulong> Targets => _selection.SelectedIds;

    private async Task DeleteAsync()
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

    private async Task MoveAsync(bool up)
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

    private async Task DuplicateAsync()
    {
        foreach (var id in Targets.ToList())
        {
            var parentId = Find(id)?.parentId ?? 0UL;
            var copy = await DescribeForClipboardAsync(id).ContinueOnAnyContext();
            if (copy is null)
                continue;

            await SpawnAsync(copy, parentId).ContinueOnAnyContext();
        }

        _world.MarkDirty();
        await _world.RefreshAsync().ContinueOnAnyContext();
    }

    private async Task AddAsync(bool global)
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

    private void Rename(MenuContext? context)
    {
        if ((context?.EntityId ?? _selection.PrimaryId) is { } id)
            RenameRequested?.Invoke(id);
    }

    private async Task ToggleEnabledAsync()
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

    private async Task SetGlobalAsync(bool global)
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

    private async Task CopyEntityAsync()
    {
        if (_selection.PrimaryId is not { } id)
            return;

        if (await DescribeForClipboardAsync(id).ContinueOnAnyContext() is { } copy)
            await _clipboard.PushAsync(copy).ContinueOnAnyContext();
    }

    private async Task PasteEntityAsync()
    {
        // Paste keeps the clipboard (Peek, not Pop) so the same entity can be pasted repeatedly.
        if (await _clipboard.PeekAsync<ClipboardEntity>().ContinueOnAnyContext() is not { } copy)
            return;

        var spawned = await SpawnAsync(copy, parent: 0UL).ContinueOnAnyContext();
        _world.MarkDirty();
        await _world.RefreshAsync().ContinueOnAnyContext();
        if (spawned is { } id)
            _selection.Select(id);
    }

    private async Task CopyComponentAsync(MenuContext? context)
    {
        if (ComponentTarget(context) is not (var entityId, var component))
            return;

        var copy = await DescribeForClipboardAsync(entityId).ContinueOnAnyContext();
        if (copy is not null && copy.Components.TryGetValue(component, out var value))
            await _clipboard.PushAsync(new ClipboardComponent { Component = component, Value = value })
                .ContinueOnAnyContext();
    }

    private async Task PasteComponentAsync(MenuContext? context)
    {
        var entityId = context?.EntityId ?? _selection.PrimaryId;
        if (entityId is not { } id
            || await _clipboard.PeekAsync<ClipboardComponent>().ContinueOnAnyContext() is not { } copy)
            return;

        await ApplyComponentAsync(id, copy.Component, copy.Value).ContinueOnAnyContext();
        _world.MarkDirty();
        await _world.RefreshAsync().ContinueOnAnyContext();
    }

    private async Task RemoveComponentAsync(MenuContext? context)
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

    // The (entity, component) a component verb targets, or null when nothing is addressable.
    private (ulong entityId, string component)? ComponentTarget(MenuContext? context)
    {
        var entityId = context?.EntityId ?? _selection.PrimaryId;
        return entityId is { } id && !string.IsNullOrEmpty(context?.Component)
            ? (id, context!.Component!)
            : null;
    }

    // Re-describes an entity from the engine and packages it for the clipboard / a duplicate. Scripts are not
    // copied yet — they carry per-binding asset references with their own flow; ordinary components only.
    private async Task<ClipboardEntity?> DescribeForClipboardAsync(ulong id)
    {
        var result = await _world.Entity(id).DescribeAsync(CancellationToken.None).ContinueOnAnyContext();
        if (result is not { Success: true, Value: { } data })
            return null;

        var copy = new ClipboardEntity { Name = data.Name, IsGlobal = data.IsGlobal, IsEnabled = data.IsEnabled };
        foreach (var component in data.Components.Where(c => c.Name != ScriptContainerComponent))
            copy.Components[component.Name] = component.Raw;
        return copy;
    }

    // Creates a new entity from a copied one and returns its id (null on failure). Each component is added (in
    // case the fresh entity doesn't carry it) and then overwritten with the copied value.
    private async Task<ulong?> SpawnAsync(ClipboardEntity copy, ulong parent)
    {
        var created = await _world.CreateEntityAsync(copy.Name, parent, CancellationToken.None)
            .ContinueOnAnyContext();
        if (created is not { Success: true, Value: { } entity })
        {
            await Popups.ShowErrorAsync("Couldn't create entity", created.Error!).ContinueOnAnyContext();
            return null;
        }

        foreach (var (component, value) in copy.Components)
            await ApplyComponentAsync(entity.Id, component, value).ContinueOnAnyContext();

        if (copy.IsGlobal)
            await entity.SetGlobalAsync(true, CancellationToken.None).ContinueOnAnyContext();
        if (!copy.IsEnabled)
            await entity.SetEnabledAsync(false, CancellationToken.None).ContinueOnAnyContext();

        return entity.Id;
    }

    // Ensures a component exists on the entity and then writes the typed value into it. add-component is
    // best-effort (it fails when the component is already present, which is fine — the set still lands).
    private async Task ApplyComponentAsync(ulong entityId, string component, JObject value)
    {
        var entity = _world.Entity(entityId);
        await entity.AddComponentAsync(component, CancellationToken.None).ContinueOnAnyContext();
        var result = await entity.Component(component).SetAsync(value, CancellationToken.None)
            .ContinueOnAnyContext();
        if (!result.Success)
            await Popups.ShowErrorAsync("Couldn't paste component", result.Error!).ContinueOnAnyContext();
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
