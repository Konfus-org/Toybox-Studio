using System;
using Newtonsoft.Json;

namespace Toybox.Studio.Utils;

/// <summary>
/// (De)serialises a <see cref="Icon"/> as its Lucide name (<c>"Move3d"</c>, <c>"Box"</c>, …) rather than a
/// raw enum ordinal — so a type can hold the strong enum in memory while its JSON contract stays the icon name the
/// engine and saved settings already use. An unknown/absent name reads as <see cref="Icon.None"/>. Applied
/// at the wire boundaries (e.g. the <c>reflect.catalog</c> reply, a persisted asset category).
/// </summary>
public sealed class IconJsonConverter : JsonConverter<Icon>
{
    public override Icon ReadJson(
        JsonReader reader, Type objectType, Icon existingValue, bool hasExistingValue, JsonSerializer serializer) =>
        Icons.Parse(reader.Value as string);

    public override void WriteJson(JsonWriter writer, Icon value, JsonSerializer serializer) =>
        writer.WriteValue(Icons.ToWire(value));
}
