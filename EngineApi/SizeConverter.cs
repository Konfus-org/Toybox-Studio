using Newtonsoft.Json.Linq;

namespace Toybox.Studio.EngineApi;

/// <summary>The wire codec for <see cref="Size"/>: a <c>{width, height}</c> object. The converters
/// that carry one (viewports, render targets, the graphics settings) compose the statics.</summary>
public sealed class SizeConverter : IWireConverter<Size>
{
    public Size Read(JToken value) => ReadSize(value);

    public JToken Write(Size value) => WriteSize(value);

    public static JToken WriteSize(Size size) => new JObject
    {
        ["width"] = size.Width,
        ["height"] = size.Height,
    };

    public static Size ReadSize(JToken? token) =>
        token is JObject value
            ? new Size(WireValue.ReadInt(value["width"]), WireValue.ReadInt(value["height"]))
            : default;
}
