using System;
using Avalonia.Media;
using Newtonsoft.Json;

namespace Toybox.Studio.Services.Theming;

/// <summary>
/// Serialises an Avalonia <see cref="Color"/> as a hex string (<c>#RRGGBB</c>, or <c>#RRGGBBAA</c> when it
/// carries alpha) — the one place colours touch text. Unparseable input falls back to magenta so a bad
/// value is loud on screen rather than silently wrong.
/// </summary>
public sealed class ColorJsonConverter : JsonConverter<Color>
{
    public override Color ReadJson(
        JsonReader reader,
        Type objectType,
        Color existingValue,
        bool hasExistingValue,
        JsonSerializer serializer) =>
        reader.Value is string hex ? FromHex(hex) : Colors.Magenta;

    public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer) =>
        writer.WriteValue(ToHex(value));

    public static Color FromHex(string hex) =>
        Color.TryParse(hex, out var color) ? color : Colors.Magenta;

    public static string ToHex(Color c) =>
        c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
}
