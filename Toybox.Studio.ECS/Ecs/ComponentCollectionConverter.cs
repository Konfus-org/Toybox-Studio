using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Ecs.Components;

namespace Toybox.Studio.Ecs;

/// <summary>
/// The <see cref="IEngineSyncConverter{T}"/> behind an <see cref="Entity"/>'s reflected <c>components</c> field: it turns
/// the engine's wire-keyed component map (<c>{ transform: {…}, renderer: {…} }</c>) into typed
/// <see cref="Component"/> instances and back. Each wire is minted as its typed subclass (via
/// <see cref="EngineSyncedComponentRegistry"/>) or an <see cref="UnknownComponent"/> when nothing models it, then
/// hydrated from its raw describe body. The instances come out DETACHED (no owning entity/world yet); the
/// <see cref="Entity"/> attaches + binds them once the whole tree is built.
/// </summary>
public sealed class ComponentCollectionConverter : IEngineSyncConverter<IReadOnlyList<Component>>
{
    // The wire↔type map is pure assembly reflection (no state), so one shared instance is safe — the generator
    // news the converter up per use with no ctor args, so it can't be injected.
    private static readonly EngineSyncedComponentRegistry Registry = new();

    public IReadOnlyList<Component> Read(JToken value)
    {
        var components = new List<Component>();
        if (value is not JObject map)
            return components;

        foreach (var property in map.Properties())
        {
            if (property.Value is not JObject body)
                continue;

            var type = Registry.TypeFor(property.Name) ?? typeof(UnknownComponent);
            var component = (Component)Activator.CreateInstance(type)!;
            component.Name = property.Name;
            component.HydrateFromDescribe(body);
            components.Add(component);
        }

        return components;
    }

    public JToken Write(IReadOnlyList<Component> value)
    {
        var map = new JObject();
        foreach (var component in value)
            map[component.Name] = component.Serialize();
        return map;
    }
}
