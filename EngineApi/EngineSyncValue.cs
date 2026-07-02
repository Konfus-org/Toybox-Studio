using System;
using System.Collections;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Avalonia.Media;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Project;
using Toybox.Studio.Utils.Extensions;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// Converts between a CLR field value and the engine's <i>bare</i> serialized JSON (the value the reflect RPCs
/// take and return, not the <c>{ type, value }</c> wrapper). This is the one place the wire footguns live: a
/// quaternion is a 4-float array distinct from a <see cref="Vector4"/> only by its declared type; a colour's
/// channels are nested floats in [0,1]; an asset handle is its bare id. The source generator emits calls to the
/// reader/writer matching each field's declared type, so a field's mapping is fixed in exactly one place.
/// </summary>
public static class EngineSyncValue
{
    public static JToken Write(bool value) => new JValue(value);

    public static JToken Write(int value) => new JValue(value);

    public static JToken Write(float value) => new JValue(value);

    public static JToken Write(string? value) => new JValue(value ?? string.Empty);

    public static JToken Write(Vector2 value) => new JArray(value.X, value.Y);

    public static JToken Write(Vector3 value) => new JArray(value.X, value.Y, value.Z);

    public static JToken Write(Vector4 value) => new JArray(value.X, value.Y, value.Z, value.W);

    // A quaternion is a 4-float (x, y, z, w) array; its declared type — not its shape — distinguishes it from a Vec4.
    public static JToken Write(Quaternion value) => new JArray(value.X, value.Y, value.Z, value.W);

    // A colour's channels are nested floats in [0,1]; the editor's Avalonia colour is byte ARGB, so scale across.
    public static JToken Write(Color value) => new JObject
    {
        ["r"] = value.R / 255f,
        ["g"] = value.G / 255f,
        ["b"] = value.B / 255f,
        ["a"] = value.A / 255f,
    };

    /// <summary>An enum's bare value is the engine's <c>[[name]]</c> string — the snake_case of the member name
    /// (<c>LessEqual</c> → <c>"less_equal"</c>), matching how the engine serializes and reads enums.</summary>
    public static JToken WriteEnum<TEnum>(TEnum value) where TEnum : struct, Enum =>
        new JValue(value.ToString().ToSnakeCase());

    /// <summary>An asset handle's bare value is its id.</summary>
    public static JToken WriteHandle(AssetHandle value) => new JValue(value.Id);

    /// <summary>A handle vector's elements are BARE ids (not nested <c>{ type:"handle", value }</c> nodes) — that's
    /// how the engine deserializes each <c>std::vector&lt;Handle&gt;</c> element; wrapping them leaves it empty.</summary>
    public static JToken WriteHandles(IEnumerable<AssetHandle> handles)
    {
        var array = new JArray();
        foreach (var handle in handles)
            array.Add(handle.Id);
        return array;
    }

    /// <summary>The fallback for a type without a dedicated mapping: Newtonsoft's structural serialization.</summary>
    public static JToken WriteObject<T>(T value) => value is null ? JValue.CreateNull() : JToken.FromObject(value);

    public static bool ReadBool(JToken? token, bool fallback = false) =>
        token is null || token.Type == JTokenType.Null ? fallback : token.Value<bool>();

    public static int ReadInt(JToken? token, int fallback = 0) =>
        token is null || token.Type == JTokenType.Null ? fallback : token.Value<int>();

    public static float ReadSingle(JToken? token, float fallback = 0) =>
        token is null || token.Type == JTokenType.Null ? fallback : token.Value<float>();

    public static string ReadString(JToken? token, string fallback = "") =>
        token is null || token.Type == JTokenType.Null ? fallback : token.Value<string>() ?? fallback;

    public static Vector2 ReadVector2(JToken? token) =>
        token is JArray a && a.Count >= 2 ? new Vector2(a[0].Value<float>(), a[1].Value<float>()) : default;

    public static Vector3 ReadVector3(JToken? token) =>
        token is JArray a && a.Count >= 3
            ? new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>())
            : default;

    public static Vector4 ReadVector4(JToken? token) =>
        token is JArray a && a.Count >= 4
            ? new Vector4(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>(), a[3].Value<float>())
            : default;

    public static Quaternion ReadQuaternion(JToken? token) =>
        token is JArray a && a.Count >= 4
            ? new Quaternion(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>(), a[3].Value<float>())
            : Quaternion.Identity;

    public static Color ReadColor(JToken? token)
    {
        if (token is not JObject value)
            return Colors.White;

        static byte Channel(JToken? channel, float fallback)
        {
            // The engine's attributed describe wraps each colour channel as a typed node ({ …, value }); unwrap
            // to the bare number before reading. A bare channel (the typed-model WriteBare path) passes through.
            channel = channel.Unwrap();
            return (byte)Math.Clamp((int)MathF.Round(ReadSingle(channel, fallback) * 255f), 0, 255);
        }

        return Color.FromArgb(
            Channel(value["a"], 1f), Channel(value["r"], 0f), Channel(value["g"], 0f), Channel(value["b"], 0f));
    }

    public static TEnum ReadEnum<TEnum>(JToken? token, TEnum fallback = default) where TEnum : struct, Enum =>
        (TEnum)ReadEnum(typeof(TEnum), token, fallback);

    /// <summary>Reads an enum value tolerant of BOTH the engine's <c>[[name]]</c> string (snake_case, matched
    /// case- and underscore-insensitively against the member name) and a bare integer ordinal — non-generic for
    /// the reflective <see cref="ReadBare(Type, JToken?)"/> path.</summary>
    public static object ReadEnum(Type type, JToken? token, object? fallback = null)
    {
        // Tolerate an attributed/typed wrapper ({ …, value }).
        token = token.Unwrap();

        fallback ??= Enum.ToObject(type, 0);
        if (token is null || token.Type == JTokenType.Null)
            return fallback;
        if (token.Type == JTokenType.Integer)
            return Enum.ToObject(type, token.Value<int>());
        if (token.Type == JTokenType.String)
        {
            var name = (token.Value<string>() ?? string.Empty).Replace("_", string.Empty);
            foreach (var value in Enum.GetValues(type))
                if (string.Equals(value.ToString()!.Replace("_", string.Empty), name, StringComparison.OrdinalIgnoreCase))
                    return value;
        }

        return fallback;
    }

    public static AssetHandle ReadHandle(JToken? token) =>
        token is null || token.Type == JTokenType.Null ? AssetHandle.None : AssetHandle.FromId(token.Value<ulong>());

    /// <summary>Reads a handle array — bare ids, tolerating the <c>{ …, value }</c> nodes the editor might echo.</summary>
    public static List<AssetHandle> ReadHandles(JToken? token)
    {
        var result = new List<AssetHandle>();
        if (token is JArray array)
        {
            foreach (var element in array)
            {
                var value = element.Unwrap();
                result.Add(value is null || value.Type == JTokenType.Null
                    ? AssetHandle.None
                    : AssetHandle.FromId(value.Value<ulong>()));
            }
        }

        return result;
    }

    public static T? ReadObject<T>(JToken? token) =>
        token is null || token.Type == JTokenType.Null ? default : token.ToObject<T>();

    // --- Reflective bare codec for nested value types and lists ---
    // The escape from Newtonsoft's structural form: a nested value type (RenderTarget, MaterialConfig, Lod, …)
    // is serialized member-by-member through THESE mappings, so a nested Vec3/Color/Handle/enum keeps the exact
    // bare wire shape the engine expects (an array / {r,g,b,a} / bare id / int) rather than Newtonsoft's default.
    // Only ever runs on plain value types and their members — never on a top-level EngineSyncedObject (those use the
    // generated CollectSynced) — so it needs no knowledge of the component/asset framework bases.

    /// <summary>Serializes an arbitrary value to its engine bare JSON by reflection: primitives, enums (int),
    /// vectors, quaternion, colour, and asset handle map as their dedicated writers do; an <see cref="IEnumerable"/>
    /// becomes a <see cref="JArray"/> of bare elements; any other object becomes a <see cref="JObject"/> of its
    /// public readable properties keyed by snake_case wire name (recursively).</summary>
    public static JToken WriteBare(object? value)
    {
        switch (value)
        {
            case null: return JValue.CreateNull();
            // A raw JToken field (e.g. a variant payload kept verbatim) passes through unchanged; cloned so it
            // never acquires a second JSON parent.
            case JToken raw: return raw.DeepClone();
            case bool b: return Write(b);
            case int i: return Write(i);
            case uint u: return new JValue(u);
            case long l: return new JValue(l);
            case ulong ul: return new JValue(ul);
            case float f: return Write(f);
            case double d: return new JValue(d);
            case string s: return Write(s);
            case Vector2 v2: return Write(v2);
            case Vector3 v3: return Write(v3);
            case Vector4 v4: return Write(v4);
            case Quaternion q: return Write(q);
            case Color c: return Write(c);
            case AssetHandle h: return WriteHandle(h);
            case Enum e: return new JValue(e.ToString().ToSnakeCase());
        }

        // A string-keyed map (an entity's wire-keyed component bodies: { transform: {…}, sky: {…} }) becomes a
        // JObject keyed by the verbatim string keys (already engine wire names — not re-snaked). Checked before the
        // IEnumerable case, since a dictionary is itself enumerable (of key/value pairs) and would otherwise
        // mis-serialize to a JArray.
        if (value is IDictionary map)
        {
            var mapBody = new JObject();
            foreach (DictionaryEntry entry in map)
                mapBody[entry.Key?.ToString() ?? string.Empty] = WriteBare(entry.Value);
            return mapBody;
        }

        if (value is IEnumerable items)
        {
            var array = new JArray();
            foreach (var item in items)
                array.Add(WriteBare(item));
            return array;
        }

        var body = new JObject();
        foreach (var property in DataProperties(value.GetType()))
            body[property.Name.ToSnakeCase()] = WriteBare(property.GetValue(value));
        return body;
    }

    /// <summary>The typed inverse of <see cref="WriteBare"/>: reads <paramref name="token"/> into a fresh
    /// <typeparamref name="T"/> (the generic wrapper the generator emits for nested/list fields). Non-null for the
    /// nested value / list types the generator routes here (a list reads to an empty list, an object to a fresh
    /// instance), matching the non-nullable fields it assigns.</summary>
    public static T ReadBare<T>(JToken? token) => (T)ReadBare(typeof(T), token)!;

    /// <summary>Reads <paramref name="token"/> into a value of <paramref name="type"/> by reflection, the inverse
    /// of <see cref="WriteBare"/>. A list type (<c>List&lt;E&gt;</c> / <c>IReadOnlyList&lt;E&gt;</c>) reads a
    /// <see cref="JArray"/> of bare elements; any other class/struct constructs an instance and sets its public
    /// writable properties from the matching snake_case keys (recursively).</summary>
    public static object? ReadBare(Type type, JToken? token)
    {
        // A raw JToken field keeps the engine value verbatim (a variant payload, etc.).
        if (typeof(JToken).IsAssignableFrom(type))
            return token?.DeepClone() ?? (object)JValue.CreateNull();

        // Tolerate a wrapped node at any nesting level: the typed-describe form ({ attributes, value }) the editor
        // echoes back AND the engine describe form ({ type, value }) a deep body carries at every level (an entity
        // record's id/name/… are each { type, value }). Unwrap to the bare value before reading.
        if (token is JObject typed && typed[EngineKeys.Value] is { } inner
            && (typed.ContainsKey(EngineKeys.Attributes) || typed.ContainsKey(EngineKeys.Type)))
            token = inner;

        if (type == typeof(bool)) return ReadBool(token);
        if (type == typeof(int)) return ReadInt(token);
        if (type == typeof(uint)) return (uint)ReadInt(token);
        if (type == typeof(long)) return token is null || token.Type == JTokenType.Null ? 0L : token.Value<long>();
        if (type == typeof(ulong)) return token is null || token.Type == JTokenType.Null ? 0UL : token.Value<ulong>();
        if (type == typeof(float)) return ReadSingle(token);
        if (type == typeof(double)) return token is null || token.Type == JTokenType.Null ? 0d : token.Value<double>();
        if (type == typeof(string)) return ReadString(token);
        if (type == typeof(Vector2)) return ReadVector2(token);
        if (type == typeof(Vector3)) return ReadVector3(token);
        if (type == typeof(Vector4)) return ReadVector4(token);
        if (type == typeof(Quaternion)) return ReadQuaternion(token);
        if (type == typeof(Color)) return ReadColor(token);
        if (type == typeof(AssetHandle)) return ReadHandle(token);
        if (type.IsEnum) return ReadEnum(type, token);

        // A string-keyed map (the inverse of WriteBare's IDictionary case) reads a JObject into a
        // Dictionary<string, V>. Checked before the list case, since a dictionary is also an enumerable.
        if (DictionaryValueTypeOf(type) is { } valueType)
        {
            var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType);
            var dict = (IDictionary)Activator.CreateInstance(dictType)!;
            if (token is JObject mapObj)
                foreach (var entry in mapObj.Properties())
                    dict[entry.Name] = ReadBare(valueType, entry.Value);
            return dict;
        }

        if (ElementTypeOf(type) is { } element)
        {
            var listType = typeof(List<>).MakeGenericType(element);
            var list = (IList)Activator.CreateInstance(listType)!;
            if (token is JArray array)
                foreach (var item in array)
                    list.Add(ReadBare(element, item));
            return list;
        }

        var instance = Activator.CreateInstance(type);
        if (token is JObject obj && instance is not null)
            foreach (var property in DataProperties(type))
                if (property.CanWrite && obj[property.Name.ToSnakeCase()] is { } value)
                    property.SetValue(instance, ReadBare(property.PropertyType, value));
        return instance;
    }

    /// <summary>The public readable instance properties that represent a type's serialized data (skipping
    /// indexers). Properties declared on the framework base chain at/above <see cref="EngineSyncedObject"/> (Raw,
    /// SyncedWires, …) are excluded, so a <see cref="EngineSyncedObject"/>-derived data type (an asset
    /// <c>MaterialInstance</c> reused as a nested value) contributes only its own modelled fields; a plain value
    /// type has no such base and contributes everything.</summary>
    internal static IEnumerable<PropertyInfo> DataProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead
                && property.GetIndexParameters().Length == 0
                && property.DeclaringType is { } owner
                && !owner.IsAssignableFrom(typeof(EngineSyncedObject)));

    // The value type V of a string-keyed map (Dictionary / IDictionary / IReadOnlyDictionary&lt;string, V&gt;), or
    // null when the type isn't such a map. Only string-keyed maps round-trip to a JObject; a non-string key has no
    // JSON property name.
    internal static Type? DictionaryValueTypeOf(Type type)
    {
        static bool IsDictionary(Type face) =>
            face.IsGenericType
            && (face.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                || face.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            && face.GetGenericArguments()[0] == typeof(string);

        if (IsDictionary(type))
            return type.GetGenericArguments()[1];
        return type.GetInterfaces().FirstOrDefault(IsDictionary)?.GetGenericArguments()[1];
    }

    // The element type E of a List&lt;E&gt; / IReadOnlyList&lt;E&gt; / IEnumerable&lt;E&gt; (but not string), or
    // null when the type isn't an enumerable the codec should expand into a JArray.
    internal static Type? ElementTypeOf(Type type)
    {
        if (type == typeof(string))
            return null;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            return type.GetGenericArguments()[0];
        return type.GetInterfaces()
            .FirstOrDefault(face =>
                face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }
}
