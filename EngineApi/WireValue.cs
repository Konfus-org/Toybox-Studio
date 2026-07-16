using Avalonia.Media;
using Newtonsoft.Json.Linq;
using System.Numerics;
using Toybox.Studio.EngineApi.Types;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// The built-in codecs between studio types and the engine's wire JSON, used by generated sync code for
/// every member without an explicit <see cref="IWireConverter{T}"/>. The wire shapes: vectors are bare
/// number arrays, quaternions four-element arrays (type identity, not shape, tells them apart), colors
/// <c>{r,g,b,a}</c> normalized floats, handles <c>{name,id}</c> objects, enums snake_case strings,
/// everything else a plain JSON value.
/// Reads are lenient — a missing or malformed token yields the fallback rather than throwing, so a
/// misbehaving peer can never crash an apply.
/// </summary>
public static class WireValue
{
    public static JToken Write(bool value) => new JValue(value);

    public static JToken Write(int value) => new JValue(value);

    public static JToken Write(long value) => new JValue(value);

    public static JToken Write(ulong value) => new JValue(value);

    public static JToken Write(float value) => new JValue(value);

    public static JToken Write(double value) => new JValue(value);

    public static JToken Write(string? value) => new JValue(value ?? string.Empty);

    public static JToken Write(Vector2 value) => new JArray(value.X, value.Y);

    public static JToken Write(Vector3 value) => new JArray(value.X, value.Y, value.Z);

    public static JToken Write(Vector4 value) => new JArray(value.X, value.Y, value.Z, value.W);

    public static JToken Write(Quaternion value) => new JArray(value.X, value.Y, value.Z, value.W);

    public static JToken Write(Color value) => new JObject
    {
        ["r"] = value.R / 255f,
        ["g"] = value.G / 255f,
        ["b"] = value.B / 255f,
        ["a"] = value.A / 255f,
    };

    public static JToken Write(Handle value) => new JObject
    {
        ["name"] = value.Name,
        ["id"] = value.Id,
    };

    public static JToken WriteEnum<TEnum>(TEnum value) where TEnum : struct, Enum =>
        new JValue(ToSnakeCase(value.ToString()));

    public static bool ReadBool(JToken? token, bool fallback = false) =>
        token is JValue { Type: JTokenType.Boolean } value ? value.Value<bool>() : fallback;

    public static int ReadInt(JToken? token, int fallback = 0) =>
        token is JValue { Type: JTokenType.Integer or JTokenType.Float } value ? value.Value<int>() : fallback;

    public static long ReadInt64(JToken? token, long fallback = 0) =>
        token is JValue { Type: JTokenType.Integer or JTokenType.Float } value ? value.Value<long>() : fallback;

    public static ulong ReadUInt64(JToken? token, ulong fallback = 0) =>
        token is JValue { Type: JTokenType.Integer } value ? value.Value<ulong>() : fallback;

    public static float ReadSingle(JToken? token, float fallback = 0f) =>
        token is JValue { Type: JTokenType.Float or JTokenType.Integer } value ? value.Value<float>() : fallback;

    public static double ReadDouble(JToken? token, double fallback = 0d) =>
        token is JValue { Type: JTokenType.Float or JTokenType.Integer } value ? value.Value<double>() : fallback;

    public static string ReadString(JToken? token, string fallback = "") =>
        token is JValue { Type: JTokenType.String } value ? value.Value<string>() ?? fallback : fallback;

    public static Vector2 ReadVector2(JToken? token) =>
        token is JArray { Count: >= 2 } array
            ? new Vector2(array[0].Value<float>(), array[1].Value<float>())
            : default;

    public static Vector3 ReadVector3(JToken? token) =>
        token is JArray { Count: >= 3 } array
            ? new Vector3(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>())
            : default;

    public static Vector4 ReadVector4(JToken? token) =>
        token is JArray { Count: >= 4 } array
            ? new Vector4(
                array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>(), array[3].Value<float>())
            : default;

    public static Quaternion ReadQuaternion(JToken? token) =>
        token is JArray { Count: >= 4 } array
            ? new Quaternion(
                array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>(), array[3].Value<float>())
            : Quaternion.Identity;

    public static Color ReadColor(JToken? token)
    {
        if (token is not JObject value)
            return Colors.White;

        static byte Channel(JToken? channel, float fallback) =>
            (byte)Math.Clamp((int)MathF.Round(ReadSingle(channel, fallback) * 255f), 0, 255);

        return Color.FromArgb(
            Channel(value["a"], 1f), Channel(value["r"], 0f), Channel(value["g"], 0f), Channel(value["b"], 0f));
    }

    public static Handle ReadHandle(JToken? token) => token switch
    {
        JObject value => new Handle(ReadString(value["name"]), ReadUInt64(value["id"])),
        // The engine's serializer writes a handle as its bare id.
        JValue { Type: JTokenType.Integer } id => new Handle("", id.Value<ulong>()),
        _ => default,
    };

    /// <summary>
    /// Looks a field up by its editor wire key, tolerating the engine's serialized dialect: the
    /// snake_case spelling of the key, and the <c>{type/attributes, value}</c> envelope the typed
    /// serializer wraps every field in. The returned token is the bare value (or null when absent),
    /// so the lenient readers above apply as usual.
    /// </summary>
    public static JToken? Field(JToken? body, string key)
    {
        if (body is not JObject value)
            return null;

        return Unwrap(value[key] ?? value[ToSnakeCase(key)]);
    }

    /// <summary>Strips the engine's <c>{type/attributes/is_default, value}</c> field envelope; a
    /// bare value (or an object that isn't an envelope) passes through untouched.</summary>
    public static JToken? Unwrap(JToken? token) =>
        token is JObject envelope
        && envelope.ContainsKey("value")
        && (envelope.ContainsKey("type")
            || envelope.ContainsKey("attributes")
            || envelope.ContainsKey("is_default"))
            ? envelope["value"]
            : token;

    public static TEnum ReadEnum<TEnum>(JToken? token, TEnum fallback = default) where TEnum : struct, Enum
    {
        if (token is JValue { Type: JTokenType.Integer } number)
            return (TEnum)Enum.ToObject(typeof(TEnum), number.Value<int>());

        if (token is JValue { Type: JTokenType.String } text)
        {
            var name = (text.Value<string>() ?? string.Empty).Replace("_", string.Empty);
            foreach (var candidate in Enum.GetValues<TEnum>())
                if (string.Equals(candidate.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    return candidate;
        }

        return fallback;
    }

    // LessEqual → "less_equal", to match the engine's snake_case enum spellings.
    private static string ToSnakeCase(string name)
    {
        Span<char> buffer = stackalloc char[name.Length * 2];
        var length = 0;
        for (var i = 0; i < name.Length; i++)
        {
            var letter = name[i];
            if (char.IsUpper(letter) && i > 0)
                buffer[length++] = '_';
            buffer[length++] = char.ToLowerInvariant(letter);
        }

        return new string(buffer[..length]);
    }
}
