using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Project;

namespace Toybox.Studio.Services.World.Components;

/// <summary>
/// Builds and reads the engine's self-describing <c>{ "type", "value" }</c> field nodes for the typed
/// component records, so each typed field maps to its wire key + token in exactly one place. Reads tolerate
/// both shapes the engine emits: lean <c>{ type, value }</c> (persisted) and the describe form
/// <c>{ attributes, value, is_default }</c> — both carry <c>"value"</c>. The engine reads a field by its
/// static type, so the type token is cosmetic on write.
/// </summary>
internal static class ComponentJson
{
    public static JObject Node(string type, JToken value) => new() { ["type"] = type, ["value"] = value };

    /// <summary>The bare value inside a field node, or the token itself when it carries no <c>value</c>.</summary>
    public static JToken? UnwrapValue(JToken? field) =>
        field is JObject obj && obj.TryGetValue("value", out var value) ? value : field;

    /// <summary>
    /// Merges the modeled field nodes over a clone of the original component body — so untyped fields the
    /// record doesn't model (the base <c>id</c>/<c>is_enabled</c>, and any field without a typed property)
    /// round-trip untouched. With no original body (a freshly-constructed record), emits just the modeled
    /// fields (the engine defaults the rest).
    /// </summary>
    public static JObject Merge(JObject? raw, IReadOnlyDictionary<string, JToken> fields)
    {
        var result = raw is null ? new JObject() : (JObject)raw.DeepClone();
        foreach (var (key, node) in fields)
            result[key] = node;
        return result;
    }

    // --- Scalar nodes ---
    public static JObject FloatNode(float value) => Node("float", value);
    public static JObject BoolNode(bool value) => Node("bool", value);
    public static JObject IntNode(int value) => Node("int", value);
    public static JObject StringNode(string value) => Node("string", value);

    // An enum serializes under the generic "object" token (its integer value).
    public static JObject EnumNode<TEnum>(TEnum value) where TEnum : struct, System.Enum =>
        Node("object", System.Convert.ToInt32(value));

    // --- Vector / quaternion / colour nodes (array- or object-valued leaves) ---
    public static JObject Vec2Node(Vector2 value) => Node("vec2", new JArray(value.X, value.Y));
    public static JObject Vec3Node(Vector3 value) => Node("vec3", new JArray(value.X, value.Y, value.Z));

    // A quaternion serializes as a 4-float array (x, y, z, w); the token is cosmetic on write.
    public static JObject QuatNode(Quaternion value) =>
        Node("quat", new JArray(value.X, value.Y, value.Z, value.W));

    // A colour's channels serialize as nested typed floats in [0,1] (matching the engine's per-field
    // serialization); the editor's Avalonia colour is byte ARGB, so scale across the boundary.
    public static JObject ColorNode(Avalonia.Media.Color value) => Node(
        "color",
        new JObject
        {
            ["r"] = FloatNode(value.R / 255f),
            ["g"] = FloatNode(value.G / 255f),
            ["b"] = FloatNode(value.B / 255f),
            ["a"] = FloatNode(value.A / 255f),
        });

    // --- Asset handles ---
    public static JObject HandleNode(AssetHandle handle) => Node("handle", handle.Id);

    public static JObject HandleArrayNode(IEnumerable<AssetHandle> handles) =>
        Node("array", new JArray(handles.Select(handle => (JToken)HandleNode(handle))));

    // --- Readers ---
    public static float ReadFloat(JToken? field, float fallback = 0)
    {
        var value = UnwrapValue(field);
        return value is null || value.Type == JTokenType.Null ? fallback : value.Value<float>();
    }

    public static bool ReadBool(JToken? field, bool fallback = false)
    {
        var value = UnwrapValue(field);
        return value is null || value.Type == JTokenType.Null ? fallback : value.Value<bool>();
    }

    public static int ReadInt(JToken? field, int fallback = 0)
    {
        var value = UnwrapValue(field);
        return value is null || value.Type == JTokenType.Null ? fallback : value.Value<int>();
    }

    public static TEnum ReadEnum<TEnum>(JToken? field, TEnum fallback) where TEnum : struct, System.Enum =>
        (TEnum)System.Enum.ToObject(typeof(TEnum), ReadInt(field, System.Convert.ToInt32(fallback)));

    public static Vector2 ReadVector2(JToken? field) =>
        UnwrapValue(field) is JArray a && a.Count >= 2 ? new Vector2(a[0].Value<float>(), a[1].Value<float>()) : default;

    public static Vector3 ReadVector3(JToken? field) =>
        UnwrapValue(field) is JArray a && a.Count >= 3
            ? new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>())
            : default;

    public static Quaternion ReadQuaternion(JToken? field) =>
        UnwrapValue(field) is JArray a && a.Count >= 4
            ? new Quaternion(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>(), a[3].Value<float>())
            : Quaternion.Identity;

    public static Avalonia.Media.Color ReadColor(JToken? field)
    {
        if (UnwrapValue(field) is not JObject value)
            return Avalonia.Media.Colors.White;

        static byte Channel(float normalized) =>
            (byte)System.Math.Clamp((int)System.MathF.Round(normalized * 255f), 0, 255);

        return Avalonia.Media.Color.FromArgb(
            value["a"] is { } alpha ? Channel(ReadFloat(alpha, 1)) : (byte)255,
            Channel(ReadFloat(value["r"])),
            Channel(ReadFloat(value["g"])),
            Channel(ReadFloat(value["b"])));
    }

    public static AssetHandle ReadHandle(JToken? field)
    {
        var value = UnwrapValue(field);
        return value is null || value.Type == JTokenType.Null
            ? AssetHandle.None
            : AssetHandle.FromId(value.Value<long>());
    }

    public static IReadOnlyList<AssetHandle> ReadHandles(JToken? field)
    {
        if (UnwrapValue(field) is not JArray array)
            return [];
        return array
            .Select(element => element is JObject ? ReadHandle(element) : AssetHandle.FromId(element.Value<long>()))
            .ToList();
    }
}
