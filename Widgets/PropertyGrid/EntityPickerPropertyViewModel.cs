using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Dialogs;
using Toybox.Studio.ECS;
using Toybox.Studio.Project;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// An entity-reference field (a <c>tbx::Entity</c> used as a component/script field, carried as the
/// "entity" type token whose value is the referenced id). Renders the referenced entity's name plus a
/// picker button that opens a chooser over the world's entities, committing the chosen entity's id back to
/// the backing token. Routed purely from the type token — no [[editor::view]] tag needed.
/// </summary>
public sealed partial class EntityPickerPropertyViewModel : PropertyViewModel
{
    private readonly World? _world;
    private readonly JsonValueSlot _slot;

    public EntityPickerPropertyViewModel(PropertyNode node, Action? commit, World? world) : base(node)
    {
        _world = world;
        _slot = new JsonValueSlot(node.Value);
        CommitChanges = commit;
        _displayName = ResolveDisplayName();

        if (world is not null)
            world.WorldUpdated += OnWorldUpdated;
    }

    [ObservableProperty]
    private string _displayName;

    public long CurrentId => _slot.Read<long?>() ?? 0;

    public bool HasReference => CurrentId != 0;

    private void OnWorldUpdated(IReadOnlyList<Entity> _) => Dispatch.To(DispatchContext.UI, RefreshDisplay);

    private void RefreshDisplay()
    {
        DisplayName = ResolveDisplayName();
        OnPropertyChanged(nameof(HasReference));
    }

    private string ResolveDisplayName()
    {
        var id = CurrentId;
        if (id == 0)
            return "(none)";

        return FindName((ulong)id) ?? $"#{id}";
    }

    /// <summary>Opens the modal entity chooser over the world's entities, then commits the pick.</summary>
    [RelayCommand]
    private async Task PickAsync()
    {
        // Reuse the asset chooser by presenting each entity as an entry (its id, name, and "Entity" kind).
        var options = Flatten()
            .Select(entity => new AssetEntry(unchecked((long)entity.Id), entity.Name, "Entity", ""))
            .ToList();

        var pick = await AssetPicker.ShowAsync("Select entity", options, CurrentId).ContinueOnSameContext();
        if (!pick.Confirmed)
            return;

        if (_slot.Set(new JValue(pick.Id)))
        {
            RefreshDisplay();
            RaiseCommit();
        }
    }

    private string? FindName(ulong id) =>
        Flatten().FirstOrDefault(entity => entity.Id == id)?.Name;

    private IEnumerable<Entity> Flatten()
    {
        if (_world is null)
            yield break;

        var stack = new Stack<Entity>(_world.Roots);
        while (stack.Count > 0)
        {
            var entity = stack.Pop();
            yield return entity;
            foreach (var child in entity.Children)
                stack.Push(child);
        }
    }
}
