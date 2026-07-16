using Newtonsoft.Json.Linq;
using System.Numerics;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types;

/// <summary>The wire codec for <see cref="Sphere"/>: a <c>{ center: [x,y,z], radius }</c> object.</summary>
public sealed class SphereConverter : IWireConverter<Sphere>
{
    public Sphere Read(JToken value) => ReadSphere(value);

    public JToken Write(Sphere value) => WriteSphere(value);

    public static Sphere ReadSphere(JToken? token) =>
        WireValue.Unwrap(token) is JObject body
            ? new Sphere(
                WireValue.ReadVector3(WireValue.Field(body, "center")),
                WireValue.ReadSingle(WireValue.Field(body, "radius")))
            : new Sphere(Vector3.Zero, 0f);

    public static JToken WriteSphere(Sphere sphere) => new JObject
    {
        ["center"] = WireValue.Write(sphere.Center),
        ["radius"] = WireValue.Write(sphere.Radius),
    };
}
