using Newtonsoft.Json.Linq;
using Toybox.Studio.Clipboards;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi.Types.Worlds;
using Toybox.Studio.Utils;

namespace Toybox.Studio.WorldTree;

/// <summary>
/// The entity clipboard/structure verbs — copy, cut, paste, duplicate, delete, make global/streamed,
/// enable/disable — acting on the current <see cref="WorldSelection"/> within the active
/// <see cref="GameState"/> world. Shared by the keybindings (the World Tree panel routes its
/// <c>edit.entity.*</c> actions here) and the world context menu, so a Ctrl+C from the keyboard and a
/// Copy from the menu run the identical path. Copy/paste is the uniform serialize/deserialize every
/// engine mirror shares: copy stores the entity's serialized body, paste spawns a fresh entity and
/// replays that body (its components recreated by name, then every value pushed) — so it needs no
/// dedicated engine verb. A no-op when no world is loaded or nothing is selected.
/// </summary>
public sealed class EntityOperations
{
    private readonly GameState _game;
    private readonly WorldSelection _selection;
    private readonly Clipboard _clipboard;
    private readonly Popups _popups;

    public EntityOperations(GameState game, WorldSelection selection, Clipboard clipboard, Popups popups)
    {
        _game = game;
        _selection = selection;
        _clipboard = clipboard;
        _popups = popups;
    }

    /// <summary>Whether the clipboard currently holds an entity (gates the Paste command/menu row).</summary>
    public Task<bool> CanPasteAsync() => _clipboard.Has<Entity>();

    /// <summary>Adds a fresh entity at the world root (streamed or global) and selects it.</summary>
    public async Task AddEntityAsync(bool global)
    {
        if (_game.Active is not { } world)
            return;

        var created = await world.AddEntityAsync(global).ContinueOnAnyContext();
        if (!created)
        {
            await _popups.ErrorAsync("Couldn't add entity", created.Error ?? "The engine rejected the add.")
                .ContinueOnSameContext();
            return;
        }

        if (created.Value is { } entity)
            _selection.Select(entity.Id);
    }

    /// <summary>Copies the primary (last-selected) entity's serialized body to the clipboard, keeping it
    /// there so the same entity can be pasted repeatedly.</summary>
    public async Task CopyAsync()
    {
        if (Primary() is { } entity)
            await _clipboard.CopyObject(entity, entity.Name).ContinueOnAnyContext();
    }

    /// <summary>Copies the primary entity, then deletes the whole selection.</summary>
    public async Task CutAsync()
    {
        await CopyAsync().ContinueOnAnyContext();
        await DeleteAsync().ContinueOnAnyContext();
    }

    /// <summary>Deletes every selected entity (each with its descendants) and prunes them from the
    /// selection.</summary>
    public async Task DeleteAsync()
    {
        if (_game.Active is not { } world)
            return;

        var entities = Selected(world);
        if (entities.Count == 0)
            return;

        var result = await world.DeleteAsync(entities).ContinueOnAnyContext();
        if (!result)
        {
            await _popups.ErrorAsync("Couldn't delete entity", result.Error ?? "The engine rejected the delete.")
                .ContinueOnSameContext();
            return;
        }

        foreach (var entity in entities)
            _selection.Remove(entity.Id);
    }

    /// <summary>Clones every selected entity (children included) and selects the last original's copy path
    /// (the refresh rebuilds the tree; selection re-resolves against it).</summary>
    public async Task DuplicateAsync()
    {
        if (_game.Active is not { } world)
            return;

        var entities = Selected(world);
        if (entities.Count == 0)
            return;

        var result = await world.DuplicateAsync(entities).ContinueOnAnyContext();
        if (!result)
            await _popups.ErrorAsync("Couldn't duplicate entity", result.Error ?? "The engine rejected the duplicate.")
                .ContinueOnSameContext();
    }

    /// <summary>Spawns a fresh entity from the clipboard body: recreates its components by name, then
    /// pushes every scalar and component value onto it, and selects it.</summary>
    public async Task PasteAsync()
    {
        if (_game.Active is not { } world)
            return;

        var pasted = await _clipboard.PasteObject<Entity>().ContinueOnAnyContext();
        if (pasted is null)
            return;

        var (body, name) = pasted.Value;
        var created = await world.CreateEntityAsync(name ?? "Entity", parent: 0UL).ContinueOnAnyContext();
        if (!created || created.Value is not { } entity)
        {
            await _popups.ErrorAsync("Couldn't paste entity", created.Error ?? "The entity could not be created.")
                .ContinueOnSameContext();
            return;
        }

        // Recreate the copied components by their registered name, then re-read so the fresh entity carries
        // them before their values are applied.
        if (body["components"] is JObject components && components.Count > 0)
        {
            foreach (var (componentName, _) in components)
                await entity.AddComponentAsync(componentName).ContinueOnAnyContext();
            await world.RefreshAsync().ContinueOnAnyContext();
            entity = world.Find(entity.Id) ?? entity;
        }

        ApplyPastedBody(entity, body);
        await world.RefreshAsync().ContinueOnAnyContext();
        _selection.Select(entity.Id);
    }

    /// <summary>Moves the selection between the streamed and global buckets.</summary>
    public async Task SetGlobalAsync(bool global)
    {
        if (_game.Active is not { } world)
            return;

        var entities = Selected(world);
        if (entities.Count == 0)
            return;

        var result = await world.SetGlobalAsync(entities, global).ContinueOnAnyContext();
        if (!result)
            await _popups.ErrorAsync("Couldn't change global state", result.Error ?? "The engine rejected the change.")
                .ContinueOnSameContext();
    }

    /// <summary>Reparents/reorders the dragged entity under <paramref name="parentId"/> (0 = the world root)
    /// at sibling <paramref name="index"/>, optionally moving it between the streamed and global buckets (a
    /// promote/demote drag). A drop onto the entity itself or its own descendant is refused — it would orphan
    /// the subtree.</summary>
    public async Task ReparentAsync(ulong entityId, ulong parentId, int index, bool? global = null)
    {
        if (_game.Active is not { } world || world.Find(entityId) is not { } entity)
            return;
        if (entityId == parentId || WouldCycle(world, entityId, parentId))
            return;

        var result = await world.MoveEntityAsync(entity, parentId, index, global).ContinueOnAnyContext();
        if (!result)
            await _popups.ErrorAsync("Couldn't move entity", result.Error ?? "The engine rejected the move.")
                .ContinueOnSameContext();
    }

    /// <summary>Toggles the primary entity's enabled flag.</summary>
    public async Task ToggleEnabledAsync()
    {
        if (Primary() is not { } entity)
            return;

        var result = await entity.SetEnabledAsync(!entity.IsEnabled).ContinueOnAnyContext();
        if (!result)
            await _popups.ErrorAsync("Couldn't change entity state", result.Error ?? "The engine rejected the change.")
                .ContinueOnSameContext();
    }

    // True when newParentId is the dragged entity's own descendant — dropping there would make the subtree
    // its own ancestor. Walks up the new parent's chain; a hit on the dragged id is a cycle.
    private static bool WouldCycle(World world, ulong entityId, ulong newParentId)
    {
        for (var id = newParentId; id != 0UL; id = world.Find(id)?.Parent ?? 0UL)
            if (id == entityId)
                return true;
        return false;
    }

    // The primary (last-selected) live entity, or null.
    private Entity? Primary() =>
        _game.Active is { } world && _selection.PrimaryId is { } id ? world.Find(id) : null;

    // The selected ids resolved to live entities in the given world, in selection order.
    private IReadOnlyList<Entity> Selected(World world) =>
        [.. _selection.SelectedIds.Select(world.Find).OfType<Entity>()];

    // Pushes the copied entity's scalar fields and each recreated component's values onto the fresh entity.
    // Identity/structure keys the paste must not drive back are skipped: id and parent are engine-owned, and
    // the component list's membership was recreated above (its per-component values are applied below).
    private static void ApplyPastedBody(Entity entity, JObject body)
    {
        var scalars = new JObject();
        foreach (var (key, value) in body)
            if (key is not ("id" or "parent" or "components"))
                scalars[key] = value;
        entity.RestoreFrom(scalars);

        if (body["components"] is not JObject components)
            return;

        foreach (var component in entity.Components)
            if (components[component.Name] is JObject componentBody)
                component.RestoreFrom(componentBody);
    }
}
