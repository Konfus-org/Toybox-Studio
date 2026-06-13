using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.ECS;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Widgets.Ecs;

/// <summary>
/// A persistent, observable view of one entity, keyed by <see cref="Id"/>. The same instance survives world
/// refreshes — <see cref="UpdateFrom"/> reconciles it against the latest snapshot in place — so a selection
/// held against it stays valid without a re-find/reselect.
/// </summary>
public sealed partial class EntityViewModel : ObservableObject
{
    private readonly EngineRpc _engine;
    private readonly Func<Task> _resync;

    public EntityViewModel(ulong id, EngineRpc engine, Func<Task> resync)
    {
        Id = id;
        _engine = engine;
        _resync = resync;
    }

    public ulong Id { get; }

    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    [ObservableProperty]
    public partial string Tag { get; private set; } = "";

    [ObservableProperty]
    public partial string Subtitle { get; private set; } = "";

    public ObservableCollection<ComponentViewModel> Components { get; } = [];

    public ObservableCollection<EntityViewModel> Children { get; } = [];

    /// <summary>Reconciles this VM against a fresh snapshot (same id), rebuilding its components.</summary>
    public void UpdateFrom(Entity data)
    {
        Name = data.Name;
        Tag = data.Tag;
        Subtitle = string.IsNullOrEmpty(data.Tag) ? $"id {data.Id}" : $"id {data.Id} — tag '{data.Tag}'";

        Components.Clear();
        foreach (var component in data.Components)
            Components.Add(new ComponentViewModel(Id, component, _engine, _resync));
    }

    /// <summary>Replaces the child VMs (the persistent instances are reused by the world reconcile).</summary>
    public void SetChildren(IReadOnlyList<EntityViewModel> children)
    {
        Children.Clear();
        foreach (var child in children)
            Children.Add(child);
    }
}
