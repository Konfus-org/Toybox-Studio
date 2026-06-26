using Toybox.Studio.Services.Commands;
using Toybox.Studio.Services.World;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Widgets.Behaviors;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.Widgets.Ecs;

/// <summary>
/// The reusable, shared view of the engine's world: a persistent <see cref="EntityViewModel"/> graph that
/// reconciles in place against each world snapshot (the engine owns the data), plus the current selection.
/// The world tree and the entity inspector both bind to this single instance. Because entity VMs persist by
/// id and selection is held as an id, the selection survives a refresh with no manual re-find.
/// </summary>
public sealed partial class WorldViewModel : ObservableObject
{
    // The editor-driven live pull (see RefreshSelectedEntityAsync) skips an entity edited within this window:
    // the user's edit was already pushed to the engine, but a describe issued just before it landed could come
    // back with a stale value and overwrite what they're scrubbing. The next pull catches up.
    private static readonly TimeSpan EditGuard = TimeSpan.FromMilliseconds(400);

    private readonly WorldManager _world;
    private readonly WorldSelection _selection;
    private readonly ComponentCatalog _components;
    private readonly ScriptCatalog _scripts;
    private readonly Dictionary<ulong, EntityViewModel> _entities = [];

    // Guards against overlapping pulls when a request outlives the refresh cadence.
    private bool _syncing;

    // When the selected entity was last edited; the live pull defers to a recent edit (see EditGuard).
    private DateTime _lastEditUtc = DateTime.MinValue;

    // The id of a just-added entity that should drop into inline rename the moment it is reconciled into the
    // tree and selected — set by the add commands, consumed (once) by ResolveSelection.
    private ulong? _pendingRenameId;

    public WorldViewModel(
        WorldManager world,
        WorldSelection selection,
        ComponentCatalog components,
        ScriptCatalog scripts,
        EditorCommands editor)
    {
        _world = world;
        _selection = selection;
        _components = components;
        _scripts = scripts;
        world.WorldChanged += snapshot => Dispatch.To(DispatchContext.UI, () => Reconcile(snapshot.Roots));
        selection.SelectionChanged +=
            () => Dispatch.To(DispatchContext.UI, () => ResolveSelection(_selection.SelectedId, reveal: true));
        // The "Rename" context-menu verb is a view-layer action (inline edit in the tree); EditorCommands
        // raises it and the world view performs it on the matching row.
        editor.RenameRequested += id => Dispatch.To(DispatchContext.UI, () => BeginRenameEntity(id));
    }

    /// <summary>The shared entity selection (a set, to support multi-select). The world tree's selectors bind
    /// their native multi-selection to this via <c>WorldTreeSelection</c>; the inspector follows the primary
    /// through <see cref="SelectedEntity"/>.</summary>
    public WorldSelection Selection => _selection;

    public ObservableCollection<EntityViewModel> RootEntities { get; } = [];

    /// <summary>The global entities (full-lifetime residents), shown flat in the world view's Globals section
    /// above the world tree. An entity appears here xor in the tree; dragging between the two toggles it.</summary>
    public ObservableCollection<EntityViewModel> Globals { get; } = [];

    /// <summary>The flat, name-filtered entity list shown while an entity search is active (instead of the
    /// hierarchical tree), drawn from every entity in the world regardless of nesting depth.</summary>
    public ObservableCollection<EntityViewModel> SearchResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial EntityViewModel? SelectedEntity { get; set; }

    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    /// <summary>Entity-name filter for the world list; while non-empty the tree gives way to a flat result list.</summary>
    [ObservableProperty]
    public partial string EntitySearch { get; set; } = "";

    /// <summary>Inspector search; pushed into each component's grid to filter rows by header or value.</summary>
    [ObservableProperty]
    public partial string ComponentSearch { get; set; } = "";

    public bool HasWorld => RootEntities.Count > 0 || Globals.Count > 0;

    /// <summary>Whether the Globals section has anything to show (it stays visible as a drop target while a
    /// world is loaded even when empty — see the view).</summary>
    public bool HasGlobals => Globals.Count > 0;

    public bool HasSelection => SelectedEntity is not null;

    /// <summary>Whether an entity search is active (drives tree-vs-results visibility in the world view).</summary>
    public bool IsSearching => EntitySearch.Trim().Length > 0;

    public bool ShowTree => HasWorld && !IsSearching;

    public bool ShowResults => HasWorld && IsSearching;

    /// <summary>True when a search is active but nothing matched — drives the world view's empty-state ghost.</summary>
    public bool ShowNoResults => ShowResults && SearchResults.Count == 0;

    /// <summary>Whether to show the Globals section (whenever a world is loaded and not searching). It stays
    /// visible even when empty so it can serve as a drop target for promoting an entity to global.</summary>
    public bool ShowGlobals => HasWorld && !IsSearching;

    /// <summary>
    /// Optimistically reorders/reparents <paramref name="dragged"/> in the local VM graph so the tree
    /// updates the instant a drop lands — no waiting on the engine round-trip. The local move mirrors what
    /// the engine will do (<c>entity.move</c> + its sibling renumber), so the refresh that follows
    /// reconciles to an identical arrangement with the same persistent VMs and nothing visibly jumps. A
    /// no-op (deferring to the engine's rejection + refresh) when the target would create a cycle.
    /// </summary>
    public void ApplyLocalMove(EntityViewModel dragged, ulong parent, int index, bool targetIsGlobal)
    {
        // The destination parent VM, but only when it is shown in the section the entity is landing in;
        // otherwise (parent in the other section, or no parent) the entity surfaces as a root of the target
        // section, exactly as Reconcile would re-root it.
        var owner = parent != 0 ? _entities.GetValueOrDefault(parent) : null;
        if (owner is not null && owner.IsGlobal != targetIsGlobal)
            owner = null;

        // Never move an entity beneath itself or its own descendant — that would weave a cycle into the VM
        // tree and the TreeView would recurse forever. The engine rejects it too; let its error + refresh
        // restore truth.
        for (var ancestor = owner; ancestor is not null; ancestor = ancestor.Parent)
            if (ReferenceEquals(ancestor, dragged))
                return;

        // Detach from wherever it sits now (its current parent's children, or its current section root).
        var source = dragged.Parent?.Children ?? (dragged.IsGlobal ? Globals : RootEntities);
        source.Remove(dragged);

        // The drop index was computed against the sibling list with the dragged entity excluded (matching the
        // engine), and we've just removed it, so the destination already excludes it — insert straight at the
        // clamped slot.
        var destination = owner?.Children ?? (targetIsGlobal ? Globals : RootEntities);
        dragged.SetGlobalLocal(targetIsGlobal);
        dragged.Parent = owner;
        if (owner is not null)
            owner.IsExpanded = true; // reveal where a reparented entity landed

        destination.Insert(Math.Clamp(index, 0, destination.Count), dragged);

        // Its ancestor chain changed, so its (and its subtree's) effective-enabled dim may have flipped.
        dragged.NotifyEffectiveEnabledChanged();
        OnPropertyChanged(nameof(HasGlobals));
        NotifyViewStates();
    }

    /// <summary>
    /// Moves an entity to a new parent (0 = root) and sibling position — optionally flipping its global
    /// bucket first — then re-syncs to engine truth in a single refresh. The world tree's drag-and-drop
    /// applies the local move first (<see cref="ApplyLocalMove"/>) and calls this to commit it.
    /// </summary>
    public async Task MoveEntityAsync(ulong id, ulong parent, int index, bool global, bool changeBucket)
    {
        var entity = _world.Entity(id);

        // Flip streamed↔global first so the move then positions the entity within the destination forest.
        if (changeBucket)
        {
            var promote = await entity.SetGlobalAsync(global, CancellationToken.None).ContinueOnSameContext();
            if (!promote.Success)
            {
                await Popups.ShowErrorAsync("Couldn't change global state", promote.Error!)
                    .ContinueOnSameContext();
                await _world.RefreshAsync().ContinueOnSameContext();
                return;
            }
        }

        var result = await entity.MoveAsync(parent, index, CancellationToken.None).ContinueOnSameContext();
        if (result.Success)
            _world.MarkDirty();
        else
            await Popups.ShowErrorAsync("Couldn't move entity", result.Error!).ContinueOnSameContext();

        await _world.RefreshAsync().ContinueOnSameContext();
    }

    /// <summary>
    /// Promotes an entity to global (drag into the Globals section) or demotes it back to streamed (drag
    /// out into the tree), then re-syncs. A no-op if already in the requested state.
    /// </summary>
    public async Task SetEntityGlobalAsync(ulong id, bool global)
    {
        var result = await _world.Entity(id).SetGlobalAsync(global, CancellationToken.None)
            .ContinueOnSameContext();
        if (result.Success)
            _world.MarkDirty();
        else
            await Popups.ShowErrorAsync("Couldn't change global state", result.Error!)
                .ContinueOnSameContext();

        await _world.RefreshAsync().ContinueOnSameContext();
    }

    /// <summary>
    /// Re-queries the selected entity's live component state from the engine and reconciles it into the
    /// existing VM in place. This is the editor-driven pull: the editor (the inspector refresh coordinator)
    /// decides WHEN to call it — while the game plays — rather than the view self-polling. No-ops when nothing
    /// is selected, when a pull is already in flight, or when the entity was just edited (the engine already
    /// has that change; pulling now risks overwriting a value mid-scrub with a stale describe).
    /// </summary>
    public async Task RefreshSelectedEntityAsync()
    {
        if (_syncing)
            return;

        var entity = SelectedEntity;
        if (entity is null || DateTime.UtcNow - _lastEditUtc < EditGuard)
            return;

        _syncing = true;
        try
        {
            var result = await _world.Entity(entity.Id)
                .DescribeAsync(CancellationToken.None)
                .ContinueOnSameContext();
            if (result is { Success: true, Value: { } data }
                && ReferenceEquals(SelectedEntity, entity))
            {
                // Fast path: push the live values into the existing grid rows in place, leaving the controls
                // (and the active filter) untouched. Only when the entity's shape changed do we pay for a
                // full rebuild and re-filter.
                if (!entity.TrySyncValues(data))
                {
                    entity.UpdateFrom(data);
                    ApplyComponentFilter();
                }
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => _world.RefreshAsync();

    /// <summary>Adds a streamed entity from the Streamed section's "+".</summary>
    [RelayCommand]
    private Task AddStreamedEntityAsync() => AddEntityAsync(global: false);

    /// <summary>Adds a full-lifetime resident from the Globals section's "+".</summary>
    [RelayCommand]
    private Task AddGlobalEntityAsync() => AddEntityAsync(global: true);

    // Creates an entity at the root of its section (so it lands at the end of that list rather than nested
    // under a possibly-collapsed selection) with a placeholder name, promotes it to global if asked, then
    // selects it and drops it straight into inline rename so the user can type over the name immediately.
    private async Task AddEntityAsync(bool global)
    {
        var result = await _world.CreateEntityAsync("Entity", parent: 0UL, CancellationToken.None)
            .ContinueOnSameContext();
        if (result is not { Success: true, Value: { } entity })
        {
            await Popups.ShowErrorAsync("Couldn't add entity", result.Error!).ContinueOnSameContext();
            return;
        }

        if (global)
        {
            var promote = await entity.SetGlobalAsync(true, CancellationToken.None)
                .ContinueOnSameContext();
            if (!promote.Success)
                await Popups.ShowErrorAsync("Couldn't make entity global", promote.Error!)
                    .ContinueOnSameContext();
        }

        _world.MarkDirty();

        // Inline-rename the new entity once it is reconciled into the tree and selected (ResolveSelection).
        _pendingRenameId = entity.Id;
        await _world.RefreshAsync().ContinueOnSameContext();
        _selection.Select(entity.Id);
    }

    // Selects an entity and drops it into inline rename — the world-tree action behind the "Rename" menu verb.
    private void BeginRenameEntity(ulong id)
    {
        _selection.Select(id);
        if (_entities.TryGetValue(id, out var entity))
            entity.BeginRename();
    }

    // Deletes the given entity (or the current selection) and its whole subtree.
    [RelayCommand]
    private async Task DeleteEntityAsync(EntityViewModel? entity)
    {
        entity ??= SelectedEntity;
        if (entity is null)
            return;

        var result = await _world.Entity(entity.Id).DestroyAsync(CancellationToken.None)
            .ContinueOnSameContext();
        if (!result.Success)
        {
            await Popups.ShowErrorAsync("Couldn't delete entity", result.Error!).ContinueOnSameContext();
            return;
        }

        _world.MarkDirty();
        await _world.RefreshAsync().ContinueOnSameContext();
    }

    /// <summary>
    /// Adds a component to the selected entity: opens the component-type picker (every registered type the
    /// entity doesn't already carry, minus the script container — scripts have their own flow), then attaches
    /// the chosen type at its defaults and re-syncs. A no-op when nothing is selected or the picker is cancelled.
    /// </summary>
    [RelayCommand]
    private async Task AddComponentAsync()
    {
        var entity = SelectedEntity;
        if (entity is null)
            return;

        var present = new HashSet<string>(
            entity.Components.Select(component => component.Name), StringComparer.Ordinal)
        {
            // The script container is offered through "Add Script", never the component picker.
            "script_container",
        };
        var options = _components.Components
            .Where(type => !present.Contains(type.Name))
            .Select(type => new CatalogItem(
                type.Name, NameHumanizer.Humanize(type.Name), type.Name, type.Icon, type.IconColor))
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pick = await CatalogPicker
            .ShowAsync("Add Component", "This entity already has every component.", options)
            .ContinueOnSameContext();
        if (pick is null)
            return;

        var result = await _world.Entity(entity.Id).AddComponentAsync(pick.Key, CancellationToken.None)
            .ContinueOnSameContext();
        if (!result.Success)
        {
            await Popups.ShowErrorAsync("Couldn't add component", result.Error!).ContinueOnSameContext();
            return;
        }

        _world.MarkDirty();
        await _world.RefreshAsync().ContinueOnSameContext();
    }

    /// <summary>
    /// Adds a script to the selected entity: opens the script picker (every script asset in the project),
    /// then attaches the chosen script — creating the entity's script container if needed — and re-syncs. A
    /// no-op when nothing is selected or the picker is cancelled.
    /// </summary>
    [RelayCommand]
    private async Task AddScriptAsync()
    {
        var entity = SelectedEntity;
        if (entity is null)
            return;

        var options = _scripts.Scripts
            .Select(asset => new CatalogItem(asset.Id.ToString(), asset.Name, "Script", "ScrollText", "GREEN"))
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pick = await CatalogPicker
            .ShowAsync("Add Script", "No scripts found in this project.", options)
            .ContinueOnSameContext();
        if (pick is null || !long.TryParse(pick.Key, out var scriptId))
            return;

        var result = await _world.Entity(entity.Id).AddScriptAsync(scriptId, CancellationToken.None)
            .ContinueOnSameContext();
        if (!result.Success)
        {
            await Popups.ShowErrorAsync("Couldn't add script", result.Error!).ContinueOnSameContext();
            return;
        }

        _world.MarkDirty();
        await _world.RefreshAsync().ContinueOnSameContext();
    }

    partial void OnSelectedEntityChanged(EntityViewModel? value)
    {
        // SelectedEntity mirrors the selection's primary (set by ResolveSelection); the selection itself is
        // driven from the tree's multi-selection (WorldTreeSelection) and the viewport, so this setter only
        // follows it — it must not write back, or reflecting a multi-selection's primary would collapse it.

        // A freshly selected entity's components start unfiltered; re-apply the active inspector search.
        ApplyComponentFilter();
    }

    // An edit the engine accepted: mark the world dirty (the viewport shows the '*') and stamp the time so the
    // live pull defers to it. Routed up from each ComponentViewModel / EntityViewModel via its onEdited hook.
    private void OnEntityEdited()
    {
        _lastEditUtc = DateTime.UtcNow;
        _world.MarkDirty();
    }

    partial void OnEntitySearchChanged(string value) => RebuildSearchResults();

    partial void OnComponentSearchChanged(string value) => ApplyComponentFilter();

    // The inspector's single search drives every component grid on the selected entity (and its scripts),
    // so the panel has one search box that filters all its grids by header or value.
    private void ApplyComponentFilter()
    {
        var filter = ComponentSearch;
        foreach (var component in SelectedEntity?.Components ?? Enumerable.Empty<ComponentViewModel>())
            component.Filter = filter;
        if (SelectedEntity?.Scripts is { } scripts)
            scripts.Filter = filter;
    }

    private void Reconcile(IReadOnlyList<EntityDescription> roots)
    {
        // Rebuilding the selector rows raises transient tree SelectionChanged events; batch them so the tree's
        // selection adapter doesn't mistake a structural change for a user edit and clobber the selection set.
        _selection.BeginBatch();
        try
        {
            var seen = new HashSet<ulong>();

            // The world tree and the Globals section are two separate forests, but they share the engine's
            // single parent/child hierarchy. Each entity belongs to the section matching its globalness and
            // nests under its same-section parent; an entity whose parent sits in the other section (or that
            // has no parent) surfaces as a root of its own section. Both forests are gathered in one recursive
            // pass so globals keep their parent/child structure instead of being flattened.
            var streamedRoots = new List<EntityViewModel>();
            var globalRoots = new List<EntityViewModel>();
            foreach (var root in roots)
                Resolve(root, parentIsGlobal: null, seen, streamedRoots, globalRoots);

            foreach (var goneId in _entities.Keys.Where(id => !seen.Contains(id)).ToList())
                _entities.Remove(goneId);

            // Reconcile in place rather than Clear()+Add: a full reset would collapse the tree's expansion and
            // scroll and flash the list. This way adding or moving one entity touches only that row.
            RootEntities.Reconcile(streamedRoots);
            Globals.Reconcile(globalRoots);

            Summary = seen.Count == 0 ? "" : $"{seen.Count} entities";
            OnPropertyChanged(nameof(HasWorld));
            OnPropertyChanged(nameof(HasGlobals));
            NotifyViewStates();

            // Keep the flat search results in step with the latest snapshot while a search is active.
            if (IsSearching)
                RebuildSearchResults();

            // Restore selection to the surviving persistent VM (the real id was preserved by the guard).
            ResolveSelection(_selection.SelectedId);
        }
        finally
        {
            _selection.EndBatch();
        }
    }

    // Resolves one entity (and its subtree) into the persistent VM graph, splitting it into the streamed and
    // global forests. A node roots its own section when it has no parent or its parent lives in the other
    // section; otherwise it nests under its same-section parent (wired up by that parent's SetChildren). The
    // Parent link is reset here and re-established only for same-section children, so it always reflects the
    // section the entity is actually shown in.
    private EntityViewModel Resolve(
        EntityDescription data,
        bool? parentIsGlobal,
        HashSet<ulong> seen,
        List<EntityViewModel> streamedRoots,
        List<EntityViewModel> globalRoots)
    {
        seen.Add(data.Id);
        if (!_entities.TryGetValue(data.Id, out var entity))
        {
            entity = new EntityViewModel(
                _world.Entity(data.Id), () => _world.RefreshAsync(), OnEntityEdited);
            _entities[data.Id] = entity;
        }

        entity.UpdateFrom(data);
        entity.Parent = null;
        if (parentIsGlobal != data.IsGlobal)
            (data.IsGlobal ? globalRoots : streamedRoots).Add(entity);

        // Resolve every child (so its VM exists and is marked seen); a node only displays the children that
        // share its section, so a cross-section child shows up as a root of the other forest instead.
        var children = data.Children
            .Select(child => Resolve(child, data.IsGlobal, seen, streamedRoots, globalRoots))
            .ToList();
        entity.SetChildren(children.Where(child => child.IsGlobal == entity.IsGlobal).ToList());
        return entity;
    }

    // reveal: expand the selected entity's ancestors so a nested node (e.g. selected from the viewport) is
    // brought into view. Only set on an actual selection change — a plain reconcile re-resolve passes false
    // so it never fights a parent the user has just collapsed.
    private void ResolveSelection(ulong? id, bool reveal = false)
    {
        SelectedEntity = id is { } value ? _entities.GetValueOrDefault(value) : null;

        if (reveal && SelectedEntity is not null)
            for (var ancestor = SelectedEntity.Parent; ancestor is not null; ancestor = ancestor.Parent)
                ancestor.IsExpanded = true;

        // A just-added entity begins inline rename the first time it resolves to a real VM (whichever of the
        // posted reconcile / selection callbacks lands second finds the flag already cleared).
        if (SelectedEntity is { } selected && _pendingRenameId == selected.Id)
        {
            _pendingRenameId = null;
            selected.BeginRename();
        }
    }

    // Rebuilds the flat result list from every entity in the world (any depth) whose name matches the query.
    private void RebuildSearchResults()
    {
        SearchResults.Clear();
        var query = EntitySearch.Trim();
        if (query.Length > 0)
        {
            foreach (var entity in _entities.Values
                .Where(entity => entity.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase))
                SearchResults.Add(entity);
        }

        NotifyViewStates();
    }

    // The tree/results/ghost visibility flags are computed from HasWorld + the search state; nudge them
    // together whenever either input moves.
    private void NotifyViewStates()
    {
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(ShowTree));
        OnPropertyChanged(nameof(ShowResults));
        OnPropertyChanged(nameof(ShowNoResults));
        OnPropertyChanged(nameof(ShowGlobals));
    }
}
