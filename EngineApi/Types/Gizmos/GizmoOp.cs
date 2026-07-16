using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.EngineApi.Types;

namespace Toybox.Studio.EngineApi.Types.Gizmos;

/// <summary>
/// One drawing instruction in a gizmo layer's retained stream, already in its wire shape: a compact
/// array whose head names the engine-side <c>tbx::Gizmos</c> method it replays into (snake_case, so the
/// vocabulary IS the engine's method list) followed by that method's arguments —
/// <c>["wire_box", [cx,cy,cz], [sx,sy,sz]]</c>. Only <see cref="GizmoRenderer"/> constructs ops, which
/// keeps every op spelling in one place; the token is written once and never mutated afterwards, so
/// sharing it across pushes is safe.
/// </summary>
public sealed record GizmoOp
{
    internal GizmoOp(JArray token) => Token = token;

    /// <summary>The op's wire token, owned by the op — readers must not mutate it.</summary>
    internal JArray Token { get; }
}
