using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// One script asset bound to an entity, mirroring the engine's <c>ScriptContainerBinding</c> —
/// serialized binding data only; runtime script instances live engine-side. The
/// <see cref="Overrides"/> body is the script's per-binding property overrides, round-tripped
/// verbatim.
/// </summary>
public sealed record ScriptContainerBinding
{
    /// <summary>The script asset this binding runs.</summary>
    public Handle Script { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>The binding's stable identity, assigned by the engine.</summary>
    public ulong BindingId { get; init; }

    public JObject Overrides { get; init; } = [];
}
