using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Ecs;

/// <summary>The wire codec for <see cref="Size"/>: a <c>{width, height}</c> object. The camera-side
/// converters that carry one compose the internal statics.</summary>
public sealed class SizeConverter : IWireConverter<Size>
{
    public Size Read(JToken value) => ReadSize(value);

    public JToken Write(Size value) => WriteSize(value);

    internal static JToken WriteSize(Size size) => new JObject
    {
        ["width"] = size.Width,
        ["height"] = size.Height,
    };

    internal static Size ReadSize(JToken? token) =>
        token is JObject value
            ? new Size(WireValue.ReadInt(value["width"]), WireValue.ReadInt(value["height"]))
            : default;
}
