using Newtonsoft.Json.Linq;
using System.Numerics;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The wire codec for <see cref="ModelPart"/>:
/// <c>{ transform: [16 floats], mesh_index, material_index, children: [uints] }</c>, and for a model's
/// <c>parts</c> list (the property that carries this converter). The transform is a column-major mat4.</summary>
public sealed class ModelPartConverter : IWireConverter<IReadOnlyList<ModelPart>>
{
    public IReadOnlyList<ModelPart> Read(JToken value) =>
        WireValue.Unwrap(value) is JArray array ? array.Select(ReadPart).ToArray() : [];

    public JToken Write(IReadOnlyList<ModelPart> value) =>
        new JArray(value.Select(WritePart).ToArray<object>());

    public static ModelPart ReadPart(JToken? token)
    {
        if (WireValue.Unwrap(token) is not JObject body)
            return new ModelPart(Matrix4x4.Identity, 0u, 0u, []);

        var children = WireValue.Unwrap(WireValue.Field(body, "children")) is JArray array
            ? array.Select(t => (uint)WireValue.ReadUInt64(t)).ToArray()
            : [];
        return new ModelPart(
            ReadMatrix(WireValue.Field(body, "transform")),
            (uint)WireValue.ReadUInt64(WireValue.Field(body, "mesh_index")),
            (uint)WireValue.ReadUInt64(WireValue.Field(body, "material_index")),
            children);
    }

    public static JToken WritePart(ModelPart part) => new JObject
    {
        ["transform"] = WriteMatrix(part.Transform),
        ["mesh_index"] = new JValue(part.MeshIndex),
        ["material_index"] = new JValue(part.MaterialIndex),
        ["children"] = new JArray(part.Children.Cast<object>().ToArray()),
    };

    // The engine serializes the mat4 as 16 floats. This reads/writes them in a fixed linear order, so a
    // round-trip is stable; note the engine's glm storage is column-major, so the C# Matrix4x4 element
    // layout is its transpose (fine for carrying the data — transpose at any point it's used as maths).
    private static Matrix4x4 ReadMatrix(JToken? token)
    {
        if (WireValue.Unwrap(token) is not JArray array || array.Count < 16)
            return Matrix4x4.Identity;

        float E(int index) => WireValue.ReadSingle(array[index]);
        return new Matrix4x4(
            E(0), E(1), E(2), E(3),
            E(4), E(5), E(6), E(7),
            E(8), E(9), E(10), E(11),
            E(12), E(13), E(14), E(15));
    }

    private static JToken WriteMatrix(Matrix4x4 m) => new JArray(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);
}
