using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// The <see cref="IEngineSyncConverter{T}"/> behind a <see cref="World"/>'s reflected <c>entities</c> field: it turns the
/// engine's flat entity array (each element an entity's describe body) into the editor's typed, nested
/// <see cref="Entity"/> tree and back. Each element becomes a typed entity (its reflected scalars + its typed
/// components hydrated); parenting is assembled from the flat <c>parent</c> ids and each sibling group sorted by
/// <c>(order, id)</c> — the engine's own sibling-renumber order — so the tree comes out in display order.
///
/// The entities come out DETACHED (no world binding); the owning <see cref="World"/> attaches + binds the whole
/// tree once it is built. This absorbs what used to be <c>WorldDescribe</c>; component icon metadata is editor-side
/// (each type's <c>[Icon]</c> attribute), so no engine <c>component_types</c> sibling map is needed.
/// </summary>
public sealed class EntityCollectionConverter : IEngineSyncConverter<IReadOnlyList<Entity>>
{
    public IReadOnlyList<Entity> Read(JToken value)
    {
        var elements = value as JArray ?? [];

        var nodes = new Dictionary<ulong, Entity>();
        foreach (var element in elements)
            if (element is JObject entity)
            {
                var node = BuildEntity(entity);
                nodes[node.Id] = node;
            }

        // Group children under their parent and roots on their own, each list sorted by (order, id) — the
        // engine's own sibling renumber order — before attaching, so the tree comes out in display order and a
        // later move touches only the dragged row.
        var childrenByParent = new Dictionary<ulong, List<Entity>>();
        var roots = new List<Entity>();
        foreach (var node in nodes.Values)
        {
            if (node.ParentId != 0 && nodes.ContainsKey(node.ParentId))
                (childrenByParent.TryGetValue(node.ParentId, out var list)
                    ? list
                    : childrenByParent[node.ParentId] = []).Add(node);
            else
                roots.Add(node);
        }

        roots.Sort(BySiblingOrder);
        foreach (var (parent, list) in childrenByParent)
        {
            list.Sort(BySiblingOrder);
            foreach (var child in list)
                nodes[parent].AddChild(child);
        }

        return roots;
    }

    /// <summary>Writes the tree back to the engine's flat entity array (each entity's serialized body) — the
    /// serialize half, used when a world's reflected body is collected.</summary>
    public JToken Write(IReadOnlyList<Entity> value)
    {
        var array = new JArray();
        foreach (var root in value)
            Flatten(root, array);
        return array;
    }

    /// <summary>Builds one detached typed <see cref="Entity"/> from a serialized entity element (id/parent read
    /// raw; the reflected scalars + typed components hydrated; components put back into registration order). Shared
    /// by the whole-tree read and a single-entity describe.</summary>
    internal static Entity BuildEntity(JObject element)
    {
        var id = Unwrap(element["id"]).Value<ulong>();
        var entity = new Entity(id);
        entity.SetParent(Unwrap(element["parent"]).Value<ulong>());
        entity.HydrateFromDescribe(element);
        entity.OrderComponents(element["component_order"] as JArray);
        return entity;
    }

    private static void Flatten(Entity entity, JArray into)
    {
        into.Add(entity.Serialize());
        foreach (var child in entity.Children)
            Flatten(child, into);
    }

    // Entities sort by their explicit engine order (the user's arrangement), falling back to id as a stable
    // tie-break — which MUST match the engine's reorder (renumbers a sibling group by (order, then id)), so a
    // move touches only the dragged row.
    private static int BySiblingOrder(Entity a, Entity b)
    {
        var byOrder = a.Order.CompareTo(b.Order);
        return byOrder != 0 ? byOrder : a.Id.CompareTo(b.Id);
    }

    // The bare value of an entity-envelope field: both the attributed ({attributes,value}) and lean ({type,value})
    // shapes put the value under the top-level "value" key, so a plain lookup covers both.
    private static JToken Unwrap(JToken? token) =>
        token is JObject obj && obj["value"] is { } value ? value : token ?? JValue.CreateNull();
}
