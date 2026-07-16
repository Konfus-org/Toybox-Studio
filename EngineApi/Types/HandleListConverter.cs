using Newtonsoft.Json.Linq;

namespace Toybox.Studio.EngineApi.Types;

/// <summary>
/// The wire codec for a list of <see cref="Handle"/>s (a model's material slots, a world's chunks, …):
/// a JSON array of the single-handle wire shape. The list is treated as one value — edits replace the
/// whole property, which pushes the whole array.
/// </summary>
public sealed class HandleListConverter : IWireConverter<IReadOnlyList<Handle>>
{
    public IReadOnlyList<Handle> Read(JToken value) =>
        value is JArray array ? [.. array.Select(WireValue.ReadHandle)] : [];

    public JToken Write(IReadOnlyList<Handle> value) =>
        new JArray(value.Select(handle => WireValue.Write(handle)));
}
