using Newtonsoft.Json.Linq;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// The wire codec for a list of strings (an app's plugin names, …): a bare JSON string array. The list
/// is treated as one value — edits replace the whole property, which pushes the whole array.
/// </summary>
public sealed class StringListConverter : IWireConverter<IReadOnlyList<string>>
{
    public IReadOnlyList<string> Read(JToken value) =>
        value is JArray array ? [.. array.Select(entry => WireValue.ReadString(entry))] : [];

    public JToken Write(IReadOnlyList<string> value) =>
        new JArray(value.Select(entry => WireValue.Write(entry)));
}
