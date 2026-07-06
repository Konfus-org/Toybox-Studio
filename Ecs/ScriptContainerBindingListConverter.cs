using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>The wire codec for a <see cref="ScriptContainer"/> binding list: an ordered array of
/// <c>{script, enabled, bindingId, overrides}</c> entries, the overrides body verbatim.</summary>
public sealed class ScriptContainerBindingListConverter : IWireConverter<IReadOnlyList<ScriptContainerBinding>>
{
    public IReadOnlyList<ScriptContainerBinding> Read(JToken value) =>
        value is JArray array ? [.. array.Select(ReadBinding)] : [];

    public JToken Write(IReadOnlyList<ScriptContainerBinding> value) =>
        new JArray(value.Select(WriteBinding));

    private static ScriptContainerBinding ReadBinding(JToken token) =>
        token is JObject body
            ? new ScriptContainerBinding
            {
                Script = WireValue.ReadHandle(body["script"]),
                Enabled = WireValue.ReadBool(body["enabled"], true),
                BindingId = WireValue.ReadUInt64(body["bindingId"]),
                Overrides = body["overrides"] is JObject overrides
                    ? (JObject)overrides.DeepClone()
                    : [],
            }
            : new ScriptContainerBinding();

    private static JToken WriteBinding(ScriptContainerBinding binding) => new JObject
    {
        ["script"] = WireValue.Write(binding.Script),
        ["enabled"] = binding.Enabled,
        ["bindingId"] = binding.BindingId,
        ["overrides"] = binding.Overrides.DeepClone(),
    };
}
