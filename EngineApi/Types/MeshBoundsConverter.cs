using Newtonsoft.Json.Linq;
using System.Numerics;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types;

/// <summary>The wire codec for <see cref="MeshBounds"/>:
/// <c>{ minimum, maximum, sphere, is_valid }</c>.</summary>
public sealed class MeshBoundsConverter : IWireConverter<MeshBounds>
{
    public MeshBounds Read(JToken value) => ReadBounds(value);

    public JToken Write(MeshBounds value) => WriteBounds(value);

    public static MeshBounds ReadBounds(JToken? token) =>
        WireValue.Unwrap(token) is JObject body
            ? new MeshBounds(
                WireValue.ReadVector3(WireValue.Field(body, "minimum")),
                WireValue.ReadVector3(WireValue.Field(body, "maximum")),
                SphereConverter.ReadSphere(WireValue.Field(body, "sphere")),
                WireValue.ReadBool(WireValue.Field(body, "is_valid")))
            : new MeshBounds(Vector3.Zero, Vector3.Zero, new Sphere(Vector3.Zero, 0f), false);

    public static JToken WriteBounds(MeshBounds bounds) => new JObject
    {
        ["minimum"] = WireValue.Write(bounds.Minimum),
        ["maximum"] = WireValue.Write(bounds.Maximum),
        ["sphere"] = SphereConverter.WriteSphere(bounds.Sphere),
        ["is_valid"] = WireValue.Write(bounds.IsValid),
    };
}
