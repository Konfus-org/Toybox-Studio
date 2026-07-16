using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using Toybox.Studio.EngineApi.Types.Worlds;
using Toybox.Studio.Events;
using Toybox.Studio.Keybindings;
using Toybox.Studio.Utils;

namespace Toybox.Studio.WorldTree;

/// <summary>
/// The world hierarchy panel: the active world's entities as two collapsible forests — the streamed spatial
/// entities and the full-lifetime <see cref="Globals"/> — each parented and sibling-ordered, with a top search
/// that swaps the trees for a flat filtered list. Entities are inspected through the per-entity nodes overlaid
/// on the world viewport (the <c>NodeGraph</c> project), not a panel-embedded inspector. It is the home of the
/// entity edit verbs — it handles the <c>edit.entity.*</c> actions (Ctrl+C/X/V, Ctrl+D, Del, F2), scoped to
/// this panel via <see cref="WorldTreeActions"/>, through the shared <see cref="EntityOperations"/> so a
/// keystroke, the Edit menu, and the world context menu are one path. Selection is the shared
/// <see cref="WorldSelection"/> (so tree, viewport, and gizmo stay in step) — the multi-select gesture merge
/// lives in the <see cref="WorldTreeSelection"/> behavior; this view-model only reveals (expands the ancestors
/// of) an externally-picked entity. The trees rebuild only when the entity set changes (a structural op
/// replaces the registry list), never on a value edit — those refresh their own row — and a rebuild preserves
/// each row's collapsed/expanded state. A singleton dockable; its view-model lives for the panel's open
/// lifetime.
/// </summary>
public sealed partial class WorldTreeViewModel :
    ObservableEventSubscriber, IEventHandler<EditorActionInvoked>, IWorldMenuTarget
{
    private readonly GameState _game;
    private readonly WorldSelection _selection;
    private readonly EntityOperations _ops;

    private World? _world;
    private IReadOnlyList<Entity> _builtEntities = [];
    private Dictionary<ulong, WorldEntityViewModel> _nodesById = [];

    // Set when an Add command runs, so the freshly-created entity drops straight into inline rename once the
    // add's refresh rebuilds the tree (type over the "Entity" placeholder, as the old tree did).
    private bool _renameNextAdded;

    public WorldTreeViewModel(
        GameState game, WorldSelection selection, EntityOperations ops, EventDispatcher events)
        : base(events)
    {
        _game = game;
        _selection = selection;
        _ops = ops;

        _game.ActiveChanged += OnActiveChanged;
        _selection.Changed += OnSelectionChanged;
        OnActiveChanged();
    }

    /// <summary>The streamed spatial entities' top-level rows (each carrying its subtree).</summary>
    public ObservableCollection<WorldEntityViewModel> Streamed { get; } = [];

    /// <summary>The full-lifetime resident (global) entities' top-level rows.</summary>
    public ObservableCollection<WorldEntityViewModel> Globals { get; } = [];

    /// <summary>The flat, name-filtered results shown while searching (in place of the two trees).</summary>
    public ObservableCollection<WorldEntityViewModel> SearchResults { get; } = [];

    /// <summary>The entity-name filter; a non-empty value swaps the trees for the flat results.</summary>
    [ObservableProperty]
    public partial string EntitySearch { get; set; } = "";

    /// <summary>The shared selection the tree binds its multi-select to (via <see cref="WorldTreeSelection"/>).</summary>
    public WorldSelection Selection => _selection;

    /// <summary>Whether a world is loaded (gates the empty-state ghost and the Add buttons).</summary>
    public bool HasWorld => _world is not null;

    /// <summary>Whether any global entities exist (the Globals section shows a hint when empty).</summary>
    public bool HasGlobals => Globals.Count > 0;

    /// <summary>Whether any streamed entities exist (the Streamed section shows a hint when empty).</summary>
    public bool HasStreamed => Streamed.Count > 0;

    /// <summary>Whether a search filter is active.</summary>
    public bool IsSearching => EntitySearch.Trim().Length > 0;

    /// <summary>Whether the two hierarchy trees are shown (a world is loaded and not searching).</summary>
    public bool ShowTree => HasWorld && !IsSearching;

    /// <summary>Whether the flat search results are shown.</summary>
    public bool ShowResults => HasWorld && IsSearching;

    /// <summary>Whether the "no matches" ghost is shown.</summary>
    public bool ShowNoResults => ShowResults && SearchResults.Count == 0;

    // The panel itself is the world context menu's background target — a right-click on the tree's blank
    // space (no entity) shows the add / paste rows.
    ulong? IWorldMenuTarget.EntityId => null;

    WorldMenuSurface IWorldMenuTarget.Surface => WorldMenuSurface.Tree;

    public override void Dispose()
    {
        _game.ActiveChanged -= OnActiveChanged;
        _selection.Changed -= OnSelectionChanged;
        if (_world is not null)
            _world.Changed -= OnWorldChanged;
        ClearTree();
        base.Dispose();
    }

    /// <summary>Runs an entity edit action however it was invoked (a scoped keystroke or an Edit-menu
    /// click publish the same event). Ids other features own are not this handler's.</summary>
    public void Handle(in EditorActionInvoked evt)
    {
        var id = evt.ActionId;
        Dispatch.To(DispatchContext.UI, () => Run(id));
    }

    /// <summary>Reparents/reorders the dragged entity — the drag-drop behavior's one call into the domain.
    /// <paramref name="parentId"/> 0 targets the bucket root; <paramref name="global"/> non-null moves it
    /// between the streamed and global buckets.</summary>
    public void MoveEntity(ulong entityId, ulong parentId, int index, bool? global) =>
        _ops.ReparentAsync(entityId, parentId, index, global).FireAndForget();

    [RelayCommand]
    private void AddStreamedEntity()
    {
        _renameNextAdded = true;
        _ops.AddEntityAsync(global: false).FireAndForget();
    }

    [RelayCommand]
    private void AddGlobalEntity()
    {
        _renameNextAdded = true;
        _ops.AddEntityAsync(global: true).FireAndForget();
    }

    // Deletes just the clicked row's entity (the per-row delete button): make it the selection, then run the
    // shared delete so the keystroke, menu, and button are one path.
    [RelayCommand]
    private void DeleteEntity(WorldEntityViewModel? node)
    {
        if (node is null)
            return;
        _selection.Set(node.Id);
        _ops.DeleteAsync().FireAndForget();
    }

    [RelayCommand]
    private void Refresh() => _world?.RefreshAsync().FireAndForget();

    private void Run(string id)
    {
        switch (id)
        {
            case ActionIds.EntityCopy:
                _ops.CopyAsync().FireAndForget();
                break;
            case ActionIds.EntityCut:
                _ops.CutAsync().FireAndForget();
                break;
            case ActionIds.EntityPaste:
                _ops.PasteAsync().FireAndForget();
                break;
            case ActionIds.EntityDuplicate:
                _ops.DuplicateAsync().FireAndForget();
                break;
            case ActionIds.EntityDelete:
                _ops.DeleteAsync().FireAndForget();
                break;
            case ActionIds.EntityRename:
                BeginRenamePrimary();
                break;
        }
    }

    partial void OnEntitySearchChanged(string value)
    {
        RebuildSearch();
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(ShowTree));
        OnPropertyChanged(nameof(ShowResults));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    private void BeginRenamePrimary()
    {
        if (_selection.PrimaryId is { } id && _nodesById.TryGetValue(id, out var node))
            node.BeginRename();
    }

    // The active world changed: re-point at it, re-subscribe for its structural changes, and rebuild.
    private void OnActiveChanged() => Dispatch.To(DispatchContext.UI, () =>
    {
        if (_world is not null)
            _world.Changed -= OnWorldChanged;
        _world = _game.Active;
        if (_world is not null)
            _world.Changed += OnWorldChanged;
        OnPropertyChanged(nameof(HasWorld));
        RebuildTree();
    });

    // The world raised a change. A value edit keeps the same entity-registry list; only a structural op (a
    // create/destroy/duplicate/move + refresh) replaces it — rebuild only then, so a rename or gizmo drag
    // doesn't tear down the tree (and its inline edit / selection).
    private void OnWorldChanged() => Dispatch.To(DispatchContext.UI, () =>
    {
        if (_world is { } world && !ReferenceEquals(world.Entities, _builtEntities))
            RebuildTree();
    });

    // The shared selection changed (a viewport pick, a menu select): reveal the primary by expanding its
    // ancestors, so a nested entity picked elsewhere scrolls into a visible, opened branch. The row highlight
    // is the selection behavior's job.
    private void OnSelectionChanged() => Dispatch.To(DispatchContext.UI, () =>
    {
        if (_selection.PrimaryId is { } id && _nodesById.TryGetValue(id, out var node))
            for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
                ancestor.IsExpanded = true;
    });

    // Rebuilds both forests from the world's current entity registry, preserving each row's collapsed state
    // and re-applying the search filter. A structural op (add/delete/duplicate/move) drives this.
    private void RebuildTree()
    {
        var collapsed = _nodesById.Values.Where(node => !node.IsExpanded).Select(node => node.Id).ToHashSet();
        ClearTree();
        if (_world is not { } world)
        {
            RaiseTreeFlags();
            return;
        }

        _builtEntities = world.Entities;
        var nodes = world.Entities.ToDictionary(entity => entity.Id, entity => new WorldEntityViewModel(entity));
        foreach (var entity in world.Entities.OrderBy(entity => entity.Order))
        {
            var node = nodes[entity.Id];
            if (collapsed.Contains(entity.Id))
                node.IsExpanded = false;

            if (entity.Parent != 0UL && nodes.TryGetValue(entity.Parent, out var parent))
            {
                node.Parent = parent;
                parent.Children.Add(node);
            }
            else if (entity.IsGlobal)
            {
                Globals.Add(node);
            }
            else
            {
                Streamed.Add(node);
            }
        }

        _nodesById = nodes;

        if (_renameNextAdded)
        {
            _renameNextAdded = false;
            BeginRenamePrimary();
        }

        RebuildSearch();
        RaiseTreeFlags();
    }

    // The flat, case-insensitive name filter shown while searching — every entity whose name contains the
    // query, in registry order.
    private void RebuildSearch()
    {
        SearchResults.Clear();
        var query = EntitySearch.Trim();
        if (query.Length == 0)
            return;

        foreach (var node in _nodesById.Values.OrderBy(node => node.Name))
            if (node.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                SearchResults.Add(node);
    }

    private void ClearTree()
    {
        foreach (var node in _nodesById.Values)
            node.Dispose();
        _nodesById = [];
        Streamed.Clear();
        Globals.Clear();
        SearchResults.Clear();
    }

    private void RaiseTreeFlags()
    {
        OnPropertyChanged(nameof(HasGlobals));
        OnPropertyChanged(nameof(HasStreamed));
        OnPropertyChanged(nameof(ShowTree));
        OnPropertyChanged(nameof(ShowResults));
        OnPropertyChanged(nameof(ShowNoResults));
    }
}
