using System.Collections.ObjectModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Dialogs;
using Toybox.Studio.ECS;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.Widgets.Ecs;

/// <summary>
/// One component on an entity, rendered as a type-driven property grid. Reusable (not inspector-specific)
/// and self-editing: each top-level property pushes its own change to the engine via the reflector, and on
/// rejection it surfaces the engine's message and asks the world to re-sync to engine truth.
/// </summary>
public sealed class ComponentViewModel
{
    private readonly ulong _entityId;
    private readonly JObject _raw;
    private readonly EngineRpc _engine;
    private readonly Func<Task> _resync;

    public ComponentViewModel(
        ulong entityId,
        Component component,
        EngineRpc engine,
        Func<Task> resync)
    {
        _entityId = entityId;
        _raw = component.Raw;
        _engine = engine;
        _resync = resync;
        Name = component.Name;
        Icon = component.Icon;
        IconColor = component.IconColor;
        Properties = [];

        // Each top-level property gets a commit/reset bound to its name — the engine's reflect.set/reset is
        // top-level-property granular, so an edit to any leaf within it pushes just that one property.
        foreach (var node in component.Properties)
        {
            var property = node.Name;
            var viewModel = PropertyViewModelFactory.Create(node, () => OnPropertyEdited(property));
            if (!node.ReadOnly)
                viewModel.ResetToDefault = () => OnPropertyReset(property);
            Properties.Add(viewModel);
        }
    }

    public string Name { get; }

    public string? Icon { get; }

    public string? IconColor { get; }

    public ObservableCollection<PropertyViewModelBase> Properties { get; }

    private async void OnPropertyEdited(string property)
    {
        // The leaf mutated the live token inside Raw; read the property's current bare value back out.
        var bare = _raw[property] is JObject typed && typed.TryGetValue("value", out var value)
            ? value
            : _raw[property];
        if (bare is null)
            return;

        var result = await _engine
            .SetPropertyAsync(_entityId, Name, property, bare, CancellationToken.None)
            .ContinueOnSameContext();
        if (result.Success)
            return;

        await Popups.ShowErrorAsync(
            "Couldn't apply change",
            $"The engine rejected the edit to '{Name}.{property}':\n\n{result.Error}")
            .ContinueOnSameContext();
        await _resync().ContinueOnSameContext();
    }

    private async void OnPropertyReset(string property)
    {
        var result = await _engine
            .ResetPropertyAsync(_entityId, Name, property, CancellationToken.None)
            .ContinueOnSameContext();
        if (!result.Success)
        {
            await Popups.ShowErrorAsync(
                "Couldn't reset",
                $"The engine could not reset '{Name}.{property}':\n\n{result.Error}")
                .ContinueOnSameContext();
        }

        // Re-sync either way so the grid reflects engine truth (the restored value on success).
        await _resync().ContinueOnSameContext();
    }
}
