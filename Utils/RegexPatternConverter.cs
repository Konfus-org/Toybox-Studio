using Newtonsoft.Json;

namespace Toybox.Studio.Utils;

/// <summary>
/// Persists a <see cref="RegexPattern"/> as its bare pattern string rather than a
/// <c>{ "Pattern": "…" }</c> object, so a settings file reads like the regexes it holds. A missing or
/// non-string token round-trips to the empty (unconfigured) pattern.
/// </summary>
public sealed class RegexPatternConverter : JsonConverter<RegexPattern>
{
    public override RegexPattern ReadJson(
        JsonReader reader, Type objectType, RegexPattern existingValue, bool hasExistingValue,
        JsonSerializer serializer) =>
        new(reader.Value as string);

    public override void WriteJson(JsonWriter writer, RegexPattern value, JsonSerializer serializer) =>
        writer.WriteValue(value.Pattern ?? string.Empty);
}
