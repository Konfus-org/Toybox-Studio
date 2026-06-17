using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Services.Theming;

/// <summary>
/// Serialises a <see cref="ColorGradient"/> compactly and tolerantly. A solid colour writes as a bare
/// <c>"#RRGGBB"</c> string (and a bare string still reads back — so palettes authored before gradients
/// existed load unchanged), while a real gradient writes as <c>{ "Start", "End", "Angle" }</c>. The stop
/// colours themselves are hex via <see cref="ColorJsonConverter"/>; property names are read case-insensitively.
/// </summary>
public sealed class ColorGradientJsonConverter : JsonConverter<ColorGradient>
{
    public override ColorGradient? ReadJson(
        JsonReader reader,
        Type objectType,
        ColorGradient? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        switch (reader.TokenType)
        {
            case JsonToken.Null:
                return null;

            // Legacy / solid form: a plain hex string.
            case JsonToken.String:
                return ColorGradient.Solid(ColorJsonConverter.FromHex((string)reader.Value!));

            default:
                var obj = JObject.Load(reader);
                var start = ColorJsonConverter.FromHex(Read(obj, "Start") ?? "#000000");
                var end = Read(obj, "End") is { } endHex ? ColorJsonConverter.FromHex(endHex) : start;
                var angle = obj.GetValue("Angle", StringComparison.OrdinalIgnoreCase)?.Value<double?>()
                            ?? ColorGradient.DefaultAngle;
                var kind = Enum.TryParse<ColorGradientKind>(Read(obj, "Kind"), ignoreCase: true, out var k)
                    ? k
                    : ColorGradientKind.Linear;
                return new ColorGradient(start, end, angle, kind);
        }
    }

    public override void WriteJson(JsonWriter writer, ColorGradient? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        // A flat colour stays a bare string, keeping solid palette entries readable and diff-friendly.
        if (value.IsSolid)
        {
            writer.WriteValue(ColorJsonConverter.ToHex(value.Start));
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("Start");
        writer.WriteValue(ColorJsonConverter.ToHex(value.Start));
        writer.WritePropertyName("End");
        writer.WriteValue(ColorJsonConverter.ToHex(value.End));
        writer.WritePropertyName("Angle");
        writer.WriteValue(value.Angle);
        // Only emit Kind when it departs from the Linear default, keeping ordinary gradients compact.
        if (value.Kind != ColorGradientKind.Linear)
        {
            writer.WritePropertyName("Kind");
            writer.WriteValue(value.Kind.ToString());
        }

        writer.WriteEndObject();
    }

    private static string? Read(JObject obj, string name) =>
        obj.GetValue(name, StringComparison.OrdinalIgnoreCase)?.Value<string>();
}
