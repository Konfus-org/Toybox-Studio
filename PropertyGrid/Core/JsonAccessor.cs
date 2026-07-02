using System;
using System.Numerics;
using Avalonia.Media;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// A <see cref="IValueAccessor"/> backed by a live <c>JToken</c> inside a describe body — the value source for
/// engine-authored data Studio can't model in C# (plugin/unknown components, script-exposed properties, the
/// project-settings schema). It keeps the pre-reflection behaviour: edits mutate the token in place and re-hold
/// the replacement (the old <c>JsonValueSlot</c> contract), and every descendant shares the one top-level commit.
/// CLR values cross the boundary through <see cref="EngineSyncValue"/>, so a widget sees the same typed value it
/// would from a <see cref="ReflectedAccessor"/>.
/// </summary>
public sealed class JsonAccessor : IValueAccessor
{
    // The live value token (already unwrapped out of any { type, value } wrapper), mutated in place. Replaced and
    // re-held on Set so the next edit still writes the live document.
    private JToken? _token;
    private readonly string _wire;
    private readonly Action? _commit;

    // A resizable list's element template: one wrapper token cloned to append a new entry. Null for non-lists.
    private readonly JToken? _elementTemplate;

    // The describe-only inline default value (a script binding's override field), as a bare token; null when the
    // describe carried none. Exposed as <see cref="Default"/> so a caller with no per-field engine reset (scripts)
    // can reset the value to it.
    private readonly JToken? _default;

    public JsonAccessor(
        JToken? token, string wire, Action? commit, JToken? elementTemplate = null, JToken? @default = null)
    {
        _token = token;
        _wire = wire;
        _commit = commit;
        _elementTemplate = elementTemplate;
        _default = @default;
    }

    public Type ValueType => ClrTypeFor(_wire);

    public bool CanWrite => _commit is not null;

    /// <summary>
    /// The value's inline describe default as a CLR value, or null when the describe carried none. For the JSON
    /// path only a script binding's override fields carry one; used to reset such a field (which has no
    /// per-field engine reset) back to the script default.
    /// </summary>
    public object? Default => _default is null ? null : EngineSyncValue.ReadBare(ValueType, _default);

    public object? Get()
    {
        // Composites hand back their live container so a search / copy sees the whole subtree; leaves decode to
        // their CLR value through the shared codec.
        var type = ValueType;
        if (type == typeof(JObject) || type == typeof(JArray) || type == typeof(JToken))
            return _token;
        return EngineSyncValue.ReadBare(type, _token);
    }

    public void Set(object? value)
    {
        if (_token is null)
            return;

        var written = EngineSyncValue.WriteBare(Coerce(value));
        _token.Replace(written);
        _token = written;
    }

    public void Commit() => _commit?.Invoke();

    public IValueAccessor Member(PropertyDescriptor member)
    {
        var child = _token is JObject obj ? UnwrapValue(obj[member.Name]) : null;
        return new JsonAccessor(child, member.Type, _commit);
    }

    public IValueAccessor Element(int index)
    {
        var element = _token is JArray array && index >= 0 && index < array.Count
            ? UnwrapValue(array[index])
            : null;
        // An element's wire token rides on the array's element template; fall back to unknown when absent.
        var wire = ElementWire();
        return new JsonAccessor(element, wire, _commit);
    }

    public object? CreateElement() => _elementTemplate?.DeepClone();

    // The element wrapper's own structural token, so a fresh element routes to the right editor. Read from the
    // template so an array of typed elements (a vector<Handle>) elements pick correctly.
    private string ElementWire() =>
        _elementTemplate is JObject wrapper && ReadType(wrapper) is { } token ? token : EngineTypes.Unknown;

    private object? Coerce(object? value)
    {
        if (value is not IConvertible)
            return value;

        var type = ValueType;
        if (type == typeof(int)) return Convert.ToInt32(value);
        if (type == typeof(long)) return Convert.ToInt64(value);
        if (type == typeof(uint)) return Convert.ToUInt32(value);
        if (type == typeof(ulong)) return Convert.ToUInt64(value);
        if (type == typeof(float)) return Convert.ToSingle(value);
        if (type == typeof(double)) return Convert.ToDouble(value);
        if (type == typeof(bool)) return Convert.ToBoolean(value);
        if (type == typeof(string)) return value.ToString();
        return value;
    }

    // The CLR type a widget reads/writes for a given wire token. Composites use JObject/JArray markers so routing
    // stays on the descriptor's type token, not this. Enums are edited as their snake_case choice string here (the
    // JSON path has no CLR enum type), and reference tokens as an AssetHandle id.
    private static Type ClrTypeFor(string wire) => wire switch
    {
        EngineTypes.Bool => typeof(bool),
        EngineTypes.Int => typeof(long),
        EngineTypes.Uuid => typeof(ulong),
        EngineTypes.Float => typeof(float),
        EngineTypes.Double => typeof(double),
        EngineTypes.String or EngineTypes.Enum => typeof(string),
        EngineTypes.Vec2 => typeof(Vector2),
        EngineTypes.Vec3 => typeof(Vector3),
        EngineTypes.Vec4 => typeof(Vector4),
        EngineTypes.Quat => typeof(Quaternion),
        EngineTypes.Color => typeof(Color),
        EngineTypes.Handle or EngineTypes.Entity => typeof(AssetHandle),
        EngineTypes.Array => typeof(JArray),
        EngineTypes.Object => typeof(JObject),
        _ => typeof(JToken),
    };

    // Splits a { type, value } / { attributes, value } wrapper to its LIVE inner value token (so an edit mutates
    // the backing document), or returns a bare token unchanged. Mirrors the parser's Unwrap.
    private static JToken? UnwrapValue(JToken? token)
    {
        if (token is JObject obj)
        {
            if (obj.ContainsKey(EngineKeys.Attributes) && obj.TryGetValue(EngineKeys.Value, out var attributed))
                return attributed;
            if (ReadType(obj) is not null && obj.TryGetValue(EngineKeys.Value, out var lean))
                return lean;
        }

        return token;
    }

    private static string? ReadType(JObject wrapper)
    {
        if (wrapper.TryGetValue(EngineKeys.Attributes, out var attributes) && attributes is JObject attributesObject)
            return attributesObject.Value<string>(EngineKeys.Type);
        return wrapper.TryGetValue(EngineKeys.Type, out var token) && token.Type == JTokenType.String
            ? token.Value<string>()
            : null;
    }
}
