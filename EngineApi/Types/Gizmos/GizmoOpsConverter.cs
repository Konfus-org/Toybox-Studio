using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Gizmos;

/// <summary>
/// The wire codec for a gizmo layer's op stream: a JSON array of the per-op compact arrays the ops
/// already hold (see <see cref="GizmoOp"/>). The list is one value — edits replace the whole property,
/// which pushes the whole drawing.
/// </summary>
public sealed class GizmoOpsConverter : IWireConverter<IReadOnlyList<GizmoOp>>
{
    public IReadOnlyList<GizmoOp> Read(JToken value) =>
        value is JArray array ? [.. array.OfType<JArray>().Select(op => new GizmoOp(op))] : [];

    public JToken Write(IReadOnlyList<GizmoOp> value) =>
        new JArray(value.Select(op => op.Token));
}
