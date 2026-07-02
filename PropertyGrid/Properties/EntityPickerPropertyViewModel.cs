using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.Worlds;
using Toybox.Studio.Utils;
using Toybox.Studio.Ecs;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// An entity-reference field (a <c>tbx::Entity</c> used as a component/script field, carried as the "entity"
/// type token whose value is the referenced id). Renders the referenced entity's name plus a picker button
/// that opens a chooser over the world's entities, committing the chosen entity's id back to the backing
/// token. Routed purely from the type token — no [[editor::view]] tag needed. Shares its view and behaviour
/// with <see cref="HandlePickerPropertyViewModel"/> via <see cref="PickerPropertyViewModel"/>; entities have
/// no file location, so clicking a set reference simply re-opens the chooser.
/// </summary>
public sealed class EntityPickerPropertyViewModel : PickerPropertyViewModel
{
    private readonly GameState? _world;

    public EntityPickerPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor, GameState? world)
        : base(descriptor, accessor)
    {
        _world = world;
        if (world is not null)
            world.Active.Changed += OnWorldChanged;
        RefreshDisplay();
    }

    public override Icon IconName => Icon.Search;

    public override string PickTooltip => "Pick an entity";

    private void OnWorldChanged(IReadOnlyList<Entity> _) => Dispatch.To(DispatchContext.UI, RefreshDisplay);

    protected override string ResolveDisplayName()
    {
        var id = CurrentId;
        if (id == 0)
            return "None";

        return FindName(id) ?? $"#{id}";
    }

    protected override (string Title, IReadOnlyList<AssetMeta> Options) BuildChoices()
    {
        // Reuse the asset chooser by presenting each entity as an entry (its id, name, and "Entity" kind).
        var options = Flatten()
            .Select(entity => new AssetMeta(entity.Id, entity.Name, "Entity", ""))
            .ToList();
        return ("Select entity", options);
    }

    private string? FindName(ulong id) =>
        Flatten().FirstOrDefault(entity => entity.Id == id)?.Name;

    private IEnumerable<Entity> Flatten()
    {
        if (_world is null)
            yield break;

        var stack = new Stack<Entity>(_world.Active.Entities);
        while (stack.Count > 0)
        {
            var entity = stack.Pop();
            yield return entity;
            foreach (var child in entity.Children)
                stack.Push(child);
        }
    }
}
