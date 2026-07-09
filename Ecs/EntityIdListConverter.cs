using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>
/// The wire codec for a list of entity ids (the world selection): a bare JSON number array. The list
/// is treated as one value — edits replace the whole property, which pushes the whole array.
/// </summary>
public sealed class EntityIdListConverter : IWireConverter<IReadOnlyList<ulong>>
{
    public IReadOnlyList<ulong> Read(JToken value) =>
        value is JArray array ? [.. array.Select(entry => WireValue.ReadUInt64(entry))] : [];

    public JToken Write(IReadOnlyList<ulong> value) =>
        new JArray(value.Select(id => WireValue.Write(id)));
}
