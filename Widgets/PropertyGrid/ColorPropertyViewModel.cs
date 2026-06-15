using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Colour property: an editable swatch/picker over the engine's <c>{r,g,b,a}</c> (0..1 float) value. Each
/// edit writes the four channels back into the live token in place so the host's commit re-sends the whole
/// colour via <c>reflect.set</c>. Tolerant of both the object <c>{r,g,b,a}</c> and array <c>[r,g,b,a]</c>
/// forms — and of wrapped channels (<c>{ "type":"float", "value":x }</c>) — preserving whichever arrived.
/// </summary>
public sealed partial class ColorPropertyViewModel : PropertyViewModel
{
    // The live colour value (JObject or JArray) inside the backing document. It is never itself replaced —
    // only its channel tokens are — so re-reading it each edit keeps writes connected to the document.
    private readonly JToken? _value;

    public ColorPropertyViewModel(PropertyNode node) : base(node)
    {
        _value = node.Value;
        var (r, g, b, a) = ReadRgba(_value);
        // Setting the backing field (not the property) so OnColorChanged doesn't fire during construction.
        _color = Color.FromArgb(ToByte(a), ToByte(r), ToByte(g), ToByte(b));
    }

    [ObservableProperty]
    private Color _color;

    /// <summary>Hex readout (#RRGGBBAA) of the current colour, for the read-only display.</summary>
    public string Hex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}{Color.A:X2}";

    /// <summary>Swatch brush of the current colour, for the read-only display.</summary>
    public IBrush Swatch => new SolidColorBrush(Color);

    partial void OnColorChanged(Color value)
    {
        OnPropertyChanged(nameof(Hex));
        OnPropertyChanged(nameof(Swatch));

        if (IsReadOnly || _value is null)
            return;

        WriteChannel("r", 0, value.R / 255.0);
        WriteChannel("g", 1, value.G / 255.0);
        WriteChannel("b", 2, value.B / 255.0);
        WriteChannel("a", 3, value.A / 255.0);
        RaiseCommit();
    }

    // Overwrites one channel in place, keeping the document shape: an array element, a wrapped channel's
    // inner "value", or a bare object member. Re-reads from _value each call so the write always lands on
    // the token currently in the document (Replace detaches the old one).
    private void WriteChannel(string key, int index, double channel)
    {
        var replacement = new JValue(channel);
        switch (_value)
        {
            case JArray array when index < array.Count:
                array[index].Replace(replacement);
                break;
            case JObject obj when obj[key] is JObject wrapper && wrapper["value"] is { } inner:
                inner.Replace(replacement);
                break;
            case JObject obj when obj[key] is { } bare:
                bare.Replace(replacement);
                break;
        }
    }

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
