using Newtonsoft.Json.Linq;
using Toybox.Studio.Utils;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// The single seam that turns a reflected field's change into an engine RPC, applying the field's
/// <see cref="EngineSyncMode"/> timing (push now / debounce / stage-until-commit) and speaking the uniform
/// <c>reflect.*</c> verbs against a <see cref="EngineAddress"/>. A <see cref="EngineSyncedObject"/> never talks to
/// the engine directly — it hands edits here.
/// </summary>
public interface IEngineSyncScheduler
{
    /// <summary>Schedules a field's new value at <paramref name="address"/> per <paramref name="mode"/>.</summary>
    void Push(EngineAddress address, string wire, JToken value, EngineSyncMode mode, int windowMs);

    /// <summary>Flushes every staged change for <paramref name="address"/> — a <see cref="EngineSyncMode.Manual"/> value,
    /// or a pending <see cref="EngineSyncMode.Timer"/> debounce / <see cref="EngineSyncMode.OnChanged"/> throttle trailing value —
    /// returning the first failure or success.</summary>
    Task<Result> FlushAsync(EngineAddress address, CancellationToken ct = default);

    /// <summary>Writes one field (by wire) at <paramref name="address"/> in place — the immediate, awaitable push
    /// (the grid's typed commit), bypassing the per-field timing.</summary>
    Task<Result> SetAsync(EngineAddress address, string wire, JToken value, CancellationToken ct = default);

    /// <summary>Resets one field (by wire) at <paramref name="address"/> to the engine default.</summary>
    Task<Result> ResetAsync(EngineAddress address, string wire, CancellationToken ct = default);

    /// <summary>Whether one field (by wire) at <paramref name="address"/> currently holds its engine default.</summary>
    Task<Result<bool>> IsDefaultAsync(EngineAddress address, string wire, CancellationToken ct = default);

    /// <summary>Re-reads the object at <paramref name="address"/> as its raw describe body, for a manual refresh.</summary>
    Task<Result<JObject>> DescribeAsync(EngineAddress address, CancellationToken ct = default);
}
