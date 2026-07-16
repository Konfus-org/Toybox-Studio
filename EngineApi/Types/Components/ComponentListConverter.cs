using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Components;

/// <summary>
/// The wire codec for an entity's <see cref="Component"/> collection — the <c>components</c> map a
/// described entity carries, keyed by engine wire name (<c>transform</c>, <c>box_collider</c>, …).
/// Reading mints a mirror per entry through the <see cref="ComponentCatalog"/> and hydrates it (its owning
/// entity stamps its id on each before it binds — see <see cref="Entity.PrepareChild"/>); a wire name the
/// editor doesn't model yet is skipped, its body left for a future editor version. Writing serializes each
/// mirror back under its name, so a world snapshot carries every component's full state.
/// </summary>
public sealed class ComponentListConverter : IWireConverter<IReadOnlyList<Component>>
{
    public IReadOnlyList<Component> Read(JToken value)
    {
        if (value is not JObject components)
            return [];

        var list = new List<Component>();
        foreach (var (wireName, body) in components)
        {
            if (body is not JObject componentBody || ComponentCatalog.Create(wireName) is not { } component)
                continue;

            component.Name = wireName;
            component.Deserialize(componentBody);
            list.Add(component);
        }
        return list;
    }

    public JToken Write(IReadOnlyList<Component> value)
    {
        var components = new JObject();
        foreach (var component in value)
            components[component.Name] = component.Serialize();
        return components;
    }
}
