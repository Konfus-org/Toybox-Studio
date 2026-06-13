using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly World _world;
    private readonly Selection _selection;
    private readonly EngineRpc _engine;
    private readonly Dictionary<ulong, EntityViewModel> _entities = [];

    // Set while rebuilding RootEntities: clearing the collection transiently nulls the tree's SelectedItem,
    // and we must not let that two-way write clobber the real selection before we restore it.
    private bool _reconciling;

    public WorldViewModel(World world, Selection selection, EngineRpc engine)
    {
        _world = world;
        _selection = selection;
        _engine = engine;
        world.WorldUpdated += roots => Dispatch.To(DispatchContext.UI, () => Reconcile(roots));
        selection.SelectionChanged += id => Dispatch.To(DispatchContext.UI, () => ResolveSelection(id));
    }

    public ObservableCollection<EntityViewModel> RootEntities { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial EntityViewModel? SelectedEntity { get; set; }

    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    public bool HasWorld => RootEntities.Count > 0;

    public bool HasSelection => SelectedEntity is not null;

    [RelayCommand]
    private Task RefreshAsync() => _world.RefreshAsync();

    partial void OnSelectedEntityChanged(EntityViewModel? value)
    {
        if (!_reconciling)
            _selection.Select(value?.Id);
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

            RootEntities.Clear();
            foreach (var root in newRoots)
                RootEntities.Add(root);

            Summary = seen.Count == 0 ? "" : $"{seen.Count} entities";
            OnPropertyChanged(nameof(HasWorld));

            // Restore selection to the surviving persistent VM (the real id was preserved by the guard).
            ResolveSelection(_selection.SelectedId);
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
        entity.SetChildren(data.Children.Select(child => Resolve(child, seen)).ToList());
        return entity;
    }

    private void ResolveSelection(ulong? id) =>
        SelectedEntity = id is { } value ? _entities.GetValueOrDefault(value) : null;
}
