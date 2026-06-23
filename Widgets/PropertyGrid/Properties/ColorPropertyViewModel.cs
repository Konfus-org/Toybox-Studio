using System.ComponentModel;
using Avalonia.Media;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Services.Theming;
using Toybox.Studio.Widgets.Colors;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Colour property: edits the engine's <c>{r,g,b,a}</c> (0..1 float) value through the shared
/// <see cref="ColorGradientView"/> in its solid-only mode, so colours in the grid use the same colour editor
/// as the rest of the app. Each edit writes the four channels back into the live token in place so the host's
/// commit re-sends the whole colour via <c>reflect.set</c>. Tolerant of both the object <c>{r,g,b,a}</c> and
/// array <c>[r,g,b,a]</c> forms — and of wrapped channels (<c>{ "type":"float", "value":x }</c>) — preserving
/// whichever arrived. Engine colours are flat, so the editor is locked to a solid colour (no gradient).
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
        var color = Color.FromArgb(ToByte(a), ToByte(r), ToByte(g), ToByte(b));
        Editor = new ColorGradientViewModel(ColorGradient.Solid(color), solidOnly: true);
        Editor.PropertyChanged += OnEditorChanged;
    }

    /// <summary>The shared colour editor (solid-only) bound by the editable view.</summary>
    public ColorGradientViewModel Editor { get; }

    /// <summary>The current colour, taken from the editor's single stop.</summary>
    private Color Color => Editor.Start;

    /// <summary>Hex readout (#RRGGBBAA) of the current colour, for the read-only display.</summary>
    public string Hex => $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}{Color.A:X2}";

    /// <summary>Swatch brush of the current colour, for the read-only display.</summary>
    public IBrush Swatch => new SolidColorBrush(Color);

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs args)
    {
        // Only the colour stop matters here; the editor is locked solid so the gradient fields never move.
        if (args.PropertyName != nameof(ColorGradientViewModel.Start))
            return;

        OnPropertyChanged(nameof(Hex));
        OnPropertyChanged(nameof(Swatch));

        if (IsReadOnly || _value is null)
            return;

        var value = Color;
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
