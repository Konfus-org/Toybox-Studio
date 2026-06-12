using Avalonia.Media;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Read-only colour swatch + hex readout, tolerant of both the object {r,g,b,a} and array forms.
/// </summary>
public sealed class ColorPropertyViewModel : PropertyViewModelBase
{
    public ColorPropertyViewModel(PropertyNode node) : base(node)
    {
        var (r, g, b, a) = ReadRgba(node.Value);
        var color = Color.FromArgb(ToByte(a), ToByte(r), ToByte(g), ToByte(b));
        Swatch = new SolidColorBrush(color);
        Hex = $"#{ToByte(r):X2}{ToByte(g):X2}{ToByte(b):X2}{ToByte(a):X2}";
    }

    public IBrush Swatch { get; }

    public string Hex { get; }

    private static byte ToByte(double channel) => (byte)Math.Clamp(channel * 255.0, 0, 255);

    private static (double R, double G, double B, double A) ReadRgba(JToken? value)
    {
        switch (value)
        {
            case JArray array:
                double Element(int i) => i < array.Count ? array[i].Value<double>() : 0.0;
                return (Element(0), Element(1), Element(2), array.Count > 3 ? Element(3) : 1.0);
            case JObject obj:
                double Member(string key) => obj[key] is { } m ? Unwrap(m).Value<double>() : 0.0;
                return (Member("r"), Member("g"), Member("b"), obj.ContainsKey("a") ? Member("a") : 1.0);
            default:
                return (0, 0, 0, 1);
        }
    }

    // Color channels may themselves be typed wrappers ({ "type": "float", "value": x }).
    private static JToken Unwrap(JToken token) =>
        token is JObject obj && obj["value"] is { } inner ? inner : token;
}
