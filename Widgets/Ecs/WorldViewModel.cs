using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Dialogs;
using Toybox.Studio.ECS;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Widgets.Utils;

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
    private readonly Selection _selection;
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

    public WorldViewModel(World world, Selection selection, EngineRpc engine, JsonParser parser, EngineWatcher watcher)
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

    // A new entity is parented under the current selection (or added at the root when nothing is selected),
    // then becomes the selection itself so the user can name/edit it straight away.
    [RelayCommand]
    private async Task AddEntityAsync()
    {
        var parent = SelectedEntity?.Id ?? 0UL;
        var result = await _engine.CreateEntityAsync(null, parent, CancellationToken.None)
            .ContinueOnSameContext();
        if (!result.Success)
        {
            await Popups.ShowErrorAsync("Couldn't add entity", result.Error!).ContinueOnSameContext();
            return;
        }

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
                entity.UpdateFrom(data);
                // The component grids were rebuilt; re-apply the active inspector filter to the new rows.
                ApplyComponentFilter();
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
            var newRoots = roots.Select(root => Resolve(root, seen)).ToList();

            foreach (var goneId in _entities.Keys.Where(id => !seen.Contains(id)).ToList())
                _entities.Remove(goneId);

            // Global entities live in their own flat section, not the scene tree, so they are pulled out of
            // the roots here and (via Resolve) out of every node's children.
            RootEntities.Clear();
            foreach (var root in newRoots.Where(root => !root.IsGlobal))
            {
                root.Parent = null;
                RootEntities.Add(root);
            }

            Globals.Clear();
            foreach (var global in _entities.Values
                .Where(entity => entity.IsGlobal)
                .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase))
                Globals.Add(global);

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

    private EntityViewModel Resolve(Entity data, HashSet<ulong> seen)
    {
        seen.Add(data.Id);
        if (!_entities.TryGetValue(data.Id, out var entity))
        {
            entity = new EntityViewModel(data.Id, _engine, () => _world.RefreshAsync());
            _entities[data.Id] = entity;
        }

        entity.UpdateFrom(data);
        // Resolve every child (so its VM exists and is marked seen), but the tree shows only the non-global
        // ones — globals are surfaced flat in their own section instead.
        var children = data.Children.Select(child => Resolve(child, seen)).ToList();
        entity.SetChildren(children.Where(child => !child.IsGlobal).ToList());
        return entity;
    }

    private void ResolveSelection(ulong? id) =>
        SelectedEntity = id is { } value ? _entities.GetValueOrDefault(value) : null;

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
