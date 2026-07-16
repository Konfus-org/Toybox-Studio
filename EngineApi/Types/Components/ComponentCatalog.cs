using System.Reflection;
using System.Text;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;

namespace Toybox.Studio.EngineApi.Types.Components;

/// <summary>
/// Maps the engine's component wire names — the snake_case keys in a described entity's
/// <c>components</c> object (<c>transform</c>, <c>box_collider</c>, <c>script_container</c>, …) — to the
/// editor's mirror <see cref="Component"/> types, and mints a fresh mirror for one. The name is the
/// snake_case of each concrete <see cref="Component"/> subclass's type name (<c>BoxCollider</c> →
/// <c>box_collider</c>), which is exactly the name the engine registers a component under (see the keys a
/// <c>.chunk</c> carries on disk), so the mapping is discovered by reflection rather than a hand-kept table.
/// </summary>
public static class ComponentCatalog
{
    private static readonly IReadOnlyDictionary<string, Type> ByWireName = Discover();

    /// <summary>Every component wire name the editor can mirror.</summary>
    public static IEnumerable<string> WireNames => ByWireName.Keys;

    /// <summary>The wire name the engine registers <paramref name="componentType"/> under.</summary>
    public static string WireNameOf(Type componentType) => SnakeCase(componentType.Name);

    /// <summary>A fresh, unbound mirror for the component the engine calls <paramref name="wireName"/>,
    /// or null when the editor doesn't model a component by that name yet (its body is then left on the
    /// entity for a future editor version rather than dropped — see <see cref="Entity"/>).</summary>
    public static Component? Create(string wireName) =>
        ByWireName.TryGetValue(wireName, out var type)
            ? (Component?)Activator.CreateInstance(type)
            : null;

    // Every concrete, default-constructible Component subclass in this assembly, keyed by its engine wire
    // name. Abstract shape parents (Collider/Trigger/Light) and any type without a parameterless
    // constructor are skipped — neither can be a described component body.
    private static IReadOnlyDictionary<string, Type> Discover()
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in typeof(Component).Assembly.GetTypes())
        {
            if (type.IsAbstract
                || !type.IsSubclassOf(typeof(Component))
                || type.GetConstructor(Type.EmptyTypes) is null)
                continue;

            map[SnakeCase(type.Name)] = type;
        }
        return map;
    }

    // Transform → transform, BoxCollider → box_collider, PostProcessing → post_processing. Component type
    // names carry no acronyms, so the simple "underscore before each inner capital" rule reproduces the
    // engine's registered names exactly (matches WireValue's enum snake-casing).
    private static string SnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character) && index > 0)
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(character));
        }
        return builder.ToString();
    }
}
