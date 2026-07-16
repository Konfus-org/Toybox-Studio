using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The wire codec for <see cref="Mesh"/>: <c>{ vertices, indices: [uints], bounds }</c>, and
/// for a model's <c>meshes</c> list (the property that carries this converter).</summary>
public sealed class MeshConverter : IWireConverter<IReadOnlyList<Mesh>>
{
    public IReadOnlyList<Mesh> Read(JToken value) =>
        WireValue.Unwrap(value) is JArray array ? array.Select(ReadMesh).ToArray() : [];

    public JToken Write(IReadOnlyList<Mesh> value) =>
        new JArray(value.Select(WriteMesh).ToArray<object>());

    public static Mesh ReadMesh(JToken? token)
    {
        if (WireValue.Unwrap(token) is not JObject body)
            return new Mesh(new VertexBuffer([], new VertexBufferLayout([], 0u)), [], DefaultBounds());

        var indices = WireValue.Unwrap(WireValue.Field(body, "indices")) is JArray array
            ? array.Select(t => (uint)WireValue.ReadUInt64(t)).ToArray()
            : [];
        return new Mesh(
            VertexBufferConverter.ReadBuffer(WireValue.Field(body, "vertices")),
            indices,
            MeshBoundsConverter.ReadBounds(WireValue.Field(body, "bounds")));
    }

    public static JToken WriteMesh(Mesh mesh) => new JObject
    {
        ["vertices"] = VertexBufferConverter.WriteBuffer(mesh.Vertices),
        ["indices"] = new JArray(mesh.Indices.Cast<object>().ToArray()),
        ["bounds"] = MeshBoundsConverter.WriteBounds(mesh.Bounds),
    };

    private static MeshBounds DefaultBounds() =>
        new(System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero,
            new Sphere(System.Numerics.Vector3.Zero, 0f), false);
}
