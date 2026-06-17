using Toybox.Studio.Services.World;
using Toybox.Studio.Utils;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Models.Ecs;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Widgets.Behaviors;

namespace Toybox.Studio.Widgets.Ecs;

/// <summary>
/// The reusable, shared view of the engine's world: a persistent <see cref="EntityViewModel"/> graph that
/// reconciles in place against each world snapshot (the engine owns the data), plus the current selection.
/// The world tree and the entity inspector both bind to this single instance. Because entity VMs persist by
/// id and selection is held as an id, the selection survives a refresh with no manual re-find.
/// </summary>
public sealed partial class WorldViewModel : ObservableObject
{
    // While the game plays, the selected entity's live state is re-queried on this cadence so the inspector
    // tracks the running game. Coarse on purpose: enough to feel live without churning the grid every frame.
    private static readonly TimeSpan SelectionSyncInterval = TimeSpan.FromMilliseconds(500);

    private readonly World _world;
    private readonly WorldSelection _selection;
    private readonly EngineRpc _engine;
    private readonly JsonParser _parser;
    private readonly Dictionary<ulong, EntityViewModel> _entities = [];

    // Drives the live re-query of the selected entity while playing. Ticks on the UI thread.
    private readonly DispatcherTimer _selectionSyncTimer;

    // Set while rebuilding RootEntities: clearing the collection transiently nulls the tree's SelectedItem,
    // and we must not let that two-way write clobber the real selection before we restore it.
    private bool _reconciling;

    // True only while the engine is in play mode — selection sync runs only then (a stopped editor world is
    // static, so there is nothing to keep in sync).
    private bool _isPlaying;

    // Guards against overlapping syncs when a request outlives the tick interval.
    private bool _syncing;

    // The id of a just-added entity that should drop into inline rename the moment it is reconciled into the
    // tree and selected — set by the add commands, consumed (once) by ResolveSelection.
    private ulong? _pendingRenameId;

    public WorldViewModel(World world, WorldSelection selection, EngineRpc engine, JsonParser parser, EngineWatcher watcher)
    {
        _world = world;
        _selection = selection;
        _engine = engine;
        _parser = parser;
        world.WorldUpdated += roots => Dispatch.To(DispatchContext.UI, () => Reconcile(roots));
        selection.SelectionChanged += id => Dispatch.To(DispatchContext.UI, () => ResolveSelection(id));

        _selectionSyncTimer = new DispatcherTimer { Interval = SelectionSyncInterval };
        _selectionSyncTimer.Tick += (_, _) => SyncSelectedEntity();

        // The watcher already raises on the UI thread; play state alone decides whether sync runs.
        watcher.StateChanged += state => OnEngineStateChanged(state);
        _isPlaying = watcher.State == EngineState.Playing;
    }

    public ObservableCollection<EntityViewModel> RootEntities { get; } = [];

    /// <summary>The global entities (full-lifetime residents), shown flat in the world view's Globals section
    /// above the scene tree. An entity appears here xor in the tree; dragging between the two toggles it.</summary>
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

    [RelayCommand]
    private Task RefreshAsync() => _world.RefreshAsync();

    /// <summary>Adds a streamed (scene) entity from the Streamed section's "+".</summary>
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
        var result = await _engine.CreateEntityAsync("Entity", parent: 0UL, CancellationToken.None)
            .ContinueOnSameContext();
        if (!result.Success)
        {
            await Popups.ShowErrorAsync("Couldn't add entity", result.Error!).ContinueOnSameContext();
            return;
        }

        if (global)
        {
            var promote = await _engine.SetEntityGlobalAsync(result.Value, true, CancellationToken.None)
                .ContinueOnSameContext();
            if (!promote.Success)
                await Popups.ShowErrorAsync("Couldn't make entity global", promote.Error!)
                    .ContinueOnSameContext();
        }

        // Inline-rename the new entity once it is reconciled into the tree and selected (ResolveSelection).
        _pendingRenameId = result.Value;
        await _world.RefreshAsync().ContinueOnSameContext();
        _selection.Select(result.Value);
    }

    // Deletes the given entity (or the current selection) and its whole subtree.
    [RelayCommand]
    private async Task DeleteEntityAsync(EntityViewModel? entity)
    {
        entity ??= SelectedEntity;
        if (entity is null)
            return;

        var result = await _engine.DestroyEntityAsync(entity.Id, CancellationToken.None)
            .ContinueOnSameContext();
        if (!result.Success)
        {
            await Popups.ShowErrorAsync("Couldn't delete entity", result.Error!).ContinueOnSameContext();
            return;
        }

        await _world.RefreshAsync().ContinueOnSameContext();
    }

    /// <summary>
    /// Moves an entity to a new parent (0 = root) and sibling position, then re-syncs to engine truth. The
    /// world tree's drag-and-drop calls this with the parent/index it computed from the drop target.
    /// </summary>
    public async Task MoveEntityAsync(ulong id, ulong parent, int index)
    {
        var result = await _engine.MoveEntityAsync(id, parent, index, CancellationToken.None)
            .ContinueOnSameContext();
        if (!result.Success)
        {
            await Popups.ShowErrorAsync("Couldn't move entity", result.Error!).ContinueOnSameContext();
        }

        await _world.RefreshAsync().ContinueOnSameContext();
    }

    /// <summary>
    /// Promotes an entity to global (drag into the Globals section) or demotes it back to the scene (drag
    /// out into the tree), then re-syncs. A no-op if already in the requested state.
    /// </summary>
    public async Task SetEntityGlobalAsync(ulong id, bool global)
    {
        var result = await _engine.SetEntityGlobalAsync(id, global, CancellationToken.None)
            .ContinueOnSameContext();
        if (!result.Success)
        {
            await Popups.ShowErrorAsync("Couldn't change global state", result.Error!)
                .ContinueOnSameContext();
        }

        await _world.RefreshAsync().ContinueOnSameContext();
    }

    partial void OnSelectedEntityChanged(EntityViewModel? value)
    {
        if (!_reconciling)
            _selection.Select(value?.Id);

        // Only the inspected entity needs live modified-state; query it on selection rather than every
        // entity on every world refresh.
        value?.RefreshModifiedState();

        // A freshly selected entity's components start unfiltered; re-apply the active inspector search.
        ApplyComponentFilter();

        // Begin (or stop) keeping the new selection in sync with the running game, and pull its current
        // state immediately so selecting during play shows live values at once.
        UpdateSelectionSync();
    }

    private void OnEngineStateChanged(EngineState state)
    {
        _isPlaying = state == EngineState.Playing;
        UpdateSelectionSync();
    }

    // Runs the selection-sync loop exactly when it is useful: the game is playing and an entity is selected.
    // Starting it also pulls the entity's state once immediately so there is no wait for the first tick.
    private void UpdateSelectionSync()
    {
        if (_isPlaying && SelectedEntity is not null)
        {
            if (!_selectionSyncTimer.IsEnabled)
                _selectionSyncTimer.Start();
            SyncSelectedEntity();
        }
        else
        {
            _selectionSyncTimer.Stop();
        }
    }

    // Re-queries the selected entity's live component state from the engine and reconciles it into the
    // existing VM. Skips overlapping requests, and bails if play stops or the selection moves mid-flight.
    private async void SyncSelectedEntity()
    {
        if (_syncing || !_isPlaying)
            return;

        var entity = SelectedEntity;
        if (entity is null)
            return;

        _syncing = true;
        try
        {
            var result = await _engine
                .DescribeEntityAsync(entity.Id, CancellationToken.None)
                .ContinueOnSameContext();
            if (result is { Success: true, Value: { } reply }
                && _parser.ParseEntity(reply) is { } data
                && _isPlaying
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

    private void Reconcile(IReadOnlyList<Entity> roots)
    {
        _reconciling = true;
        try
        {
            var seen = new HashSet<ulong>();

            // The scene tree and the Globals section are two separate forests, but they share the engine's
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
            ListReconcile.Apply(RootEntities, streamedRoots);
            ListReconcile.Apply(Globals, globalRoots);

            Summary = seen.Count == 0 ? "" : $"{seen.Count} entities";
            OnPropertyChanged(nameof(HasWorld));
            OnPropertyChanged(nameof(HasGlobals));
            NotifyViewStates();

            // Keep the flat search results in step with the latest snapshot while a search is active.
            if (IsSearching)
                RebuildSearchResults();

            // Restore selection to the surviving persistent VM (the real id was preserved by the guard).
            ResolveSelection(_selection.SelectedId);

            // Components were rebuilt by UpdateFrom; refresh the inspected entity's modified-state. (The
            // selection reference is unchanged, so OnSelectedEntityChanged won't have fired.)
            SelectedEntity?.RefreshModifiedState();
        }
        finally
        {
            _reconciling = false;
        }
    }

    // Resolves one entity (and its subtree) into the persistent VM graph, splitting it into the streamed and
    // global forests. A node roots its own section when it has no parent or its parent lives in the other
    // section; otherwise it nests under its same-section parent (wired up by that parent's SetChildren). The
    // Parent link is reset here and re-established only for same-section children, so it always reflects the
    // section the entity is actually shown in.
    private EntityViewModel Resolve(
        Entity data,
        bool? parentIsGlobal,
        HashSet<ulong> seen,
        List<EntityViewModel> streamedRoots,
        List<EntityViewModel> globalRoots)
    {
        seen.Add(data.Id);
        if (!_entities.TryGetValue(data.Id, out var entity))
        {
            entity = new EntityViewModel(data.Id, _engine, () => _world.RefreshAsync());
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

    private void ResolveSelection(ulong? id)
    {
        SelectedEntity = id is { } value ? _entities.GetValueOrDefault(value) : null;

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
