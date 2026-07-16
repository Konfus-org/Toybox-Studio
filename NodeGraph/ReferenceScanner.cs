using System.Reflection;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi.Types.Components;
using Toybox.Studio.EngineApi.Types.Worlds;

namespace Toybox.Studio.NodeGraph;

/// <summary>
/// Derives the reference plugs a node shows, by reflection. Two grains: an entity's own <em>entity</em>
/// references (<see cref="EntityReferences"/> — the parent link, entity → entity, carrying the target id so the
/// overlay draws a wire to that entity's output), and a single component's references
/// (<see cref="ComponentReferences"/> — its <see cref="Handle"/>-typed asset properties and bound scripts,
/// shown as the component's own input plugs). An input plug is a reference a thing holds; a thing's output plug
/// (the entity, or a component) is how others reference it.
/// </summary>
public static class ReferenceScanner
{
    // Cache the Handle-typed property set per component runtime type — the component schema is fixed, so
    // the reflection walk runs once per type, not once per entity per frame.
    private static readonly Dictionary<Type, PropertyInfo[]> HandleProperties = [];

    /// <summary>The entity's own entity → entity references (its node's edge input plugs). The parent is the
    /// one reliable one — component-held entity refs are raw ids with no general marker.</summary>
    public static IReadOnlyList<NodeReference> EntityReferences(Entity entity)
    {
        var references = new List<NodeReference>();
        if (entity.Parent != 0UL)
            references.Add(new NodeReference("Parent", ReferenceKind.Entity, entity.Parent));
        return references;
    }

    /// <summary>One component's references — its input plugs, shown inline with the component: each valid
    /// asset <see cref="Handle"/> property and each bound script. These target assets/scripts (no node), so
    /// they render as lit, labelled plugs rather than cross-node wires.</summary>
    public static IReadOnlyList<NodeReference> ComponentReferences(Component component)
    {
        var references = new List<NodeReference>();

        if (component is ScriptContainer scripts)
            foreach (var binding in scripts.Scripts)
                if (binding.Script.IsValid)
                    references.Add(new NodeReference("script", ReferenceKind.Script, 0UL));

        foreach (var property in HandlePropertiesOf(component.GetType()))
            if (property.GetValue(component) is Handle { IsValid: true })
                references.Add(new NodeReference(property.Name, ReferenceKind.Asset, 0UL));

        return references;
    }

    private static PropertyInfo[] HandlePropertiesOf(Type type)
    {
        if (!HandleProperties.TryGetValue(type, out var properties))
            HandleProperties[type] = properties = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.PropertyType == typeof(Handle) && property.GetIndexParameters().Length == 0)
                .ToArray();
        return properties;
    }
}
