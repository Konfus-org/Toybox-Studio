using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Project;

namespace Toybox.Studio.Services.World.Components;

/// <summary>
/// A typed value for an engine <c>material_instance</c>: a base material/instance asset plus (later) its
/// overrides. The editor mostly builds these from a base handle (e.g. previewing a material on a mesh); the
/// override editor in the inspector keeps using the dynamic path.
/// </summary>
public readonly record struct MaterialInstance(AssetHandle Material)
{
    /// <summary>The engine <c>{ type:"material_instance", value:{ material } }</c> node.</summary>
    internal JObject ToNode() =>
        ComponentJson.Node(
            "material_instance",
            new JObject { ["material"] = ComponentJson.HandleNode(Material) });

    /// <summary>Reads a material_instance field, keeping only its base handle (overrides stay on the
    /// dynamic path).</summary>
    internal static MaterialInstance FromField(JToken? field)
    {
        var value = ComponentJson.UnwrapValue(field) as JObject;
        return new MaterialInstance(ComponentJson.ReadHandle(value?["material"]));
    }
}
