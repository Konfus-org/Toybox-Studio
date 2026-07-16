using Newtonsoft.Json.Linq;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// Everything the sync runtime needs to know about one engine-synced property, built once per property
/// by the generated code (a static field per slot): the command and wire key it syncs through, the
/// outbound timing, and the codec bridging the studio type and the wire JSON (boxed delegates — sync
/// traffic is editor-rate, so the simplicity wins over the boxing).
/// </summary>
public sealed class SyncSlot(
    string command,
    string key,
    SyncMode mode,
    int batchFrequencyMs,
    bool bindsChildren,
    Func<object?, JToken> write,
    Func<JToken, object?> read)
{
    /// <summary>The RPC method pushes go through (an <see cref="EngineCommands"/> constant).</summary>
    public string Command { get; } = command;

    /// <summary>The property's wire key (camelCase member name unless overridden).</summary>
    public string Key { get; } = key;

    /// <summary>When outbound edits reach the engine.</summary>
    public SyncMode Mode { get; } = mode;

    /// <summary>How often <see cref="SyncMode.Batched"/> edits go out, in milliseconds (zero = the
    /// scheduler's floor).</summary>
    public int BatchFrequencyMs { get; } = batchFrequencyMs;

    /// <summary>Whether the property's value is (or contains) nested <see cref="EngineObject"/>s the
    /// owner binds and whose edits it aggregates — so a change to it re-reconciles the owner's children
    /// (see <see cref="EngineObject"/>). Set by the generator for EngineObject-typed synced members.</summary>
    public bool BindsChildren { get; } = bindsChildren;

    /// <summary>Converts the studio value to its wire JSON.</summary>
    public Func<object?, JToken> Write { get; } = write;

    /// <summary>Converts wire JSON back to the studio value.</summary>
    public Func<JToken, object?> Read { get; } = read;
}
