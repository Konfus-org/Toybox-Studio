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

    // Fields read through WireValue.Field, so the engine's serialized dialect (typed field
    // envelopes) hydrates as well as the editor's own bare writes.
    public static Size ReadSize(JToken? token) =>
        WireValue.Unwrap(token) is JObject value
            ? new Size(
                WireValue.ReadInt(WireValue.Field(value, "width")),
                WireValue.ReadInt(WireValue.Field(value, "height")))
            : default;
}
