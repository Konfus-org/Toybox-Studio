using Toybox.Studio.Assets;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// The sky material used for environment rendering, mirroring the engine's <c>Sky</c>. The material is
/// a <see cref="MaterialInstance"/> held by value (not an asset reference): its body travels as one
/// wire value, so edit it by assigning an updated instance back to <see cref="Material"/>.
/// </summary>
public sealed partial class Sky : Component
{
    public Sky()
    {
        Material = new MaterialInstance();
        Type = SkyType.Sphere;
    }

    public Sky(MaterialInstance material, SkyType type = SkyType.Sphere)
    {
        Material = material;
        Type = type;
    }

    [EngineSync(Converter = typeof(MaterialInstanceConverter))]
    public partial MaterialInstance Material { get; set; }

    [EngineSync]
    public partial SkyType Type { get; set; }
}
