using Avalonia.Media;
using Newtonsoft.Json.Linq;
using System.Numerics;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// The typed value of a shader parameter — the engine's tagged variant modeled as a discriminated
/// value so the reflection grid can render it with a real value editor (a colour swatch, vector fields)
/// instead of raw JSON. Only the field named by <see cref="Kind"/> is meaningful; the rest sit at their
/// defaults. The wire shape is unchanged — a bare value (a number, a <c>{r,g,b,a}</c> object, an
/// <c>[x,y,z]</c> array) — so <see cref="ToWire"/>/<see cref="FromWire"/> round-trip exactly what the
/// engine sends, and an unmodeled shape (a matrix) survives as <see cref="MaterialValueKind.Unknown"/>
/// carrying its verbatim <see cref="Raw"/> token.
/// </summary>
public sealed record MaterialValue
{
    public MaterialValueKind Kind { get; init; } = MaterialValueKind.Float;

    public bool BoolValue { get; init; }

    public int IntValue { get; init; }

    public float FloatValue { get; init; }

    public Vector2 Vector2Value { get; init; }

    public Vector3 Vector3Value { get; init; }

    public Vector4 Vector4Value { get; init; }

    public Color ColorValue { get; init; } = Colors.White;

    /// <summary>The verbatim wire token for an <see cref="MaterialValueKind.Unknown"/> shape, so it
    /// round-trips untouched; null for the typed kinds.</summary>
    public JToken? Raw { get; init; }

    public static MaterialValue OfBool(bool value) => new() { Kind = MaterialValueKind.Bool, BoolValue = value };

    public static MaterialValue OfInt(int value) => new() { Kind = MaterialValueKind.Int, IntValue = value };

    public static MaterialValue OfFloat(float value) => new() { Kind = MaterialValueKind.Float, FloatValue = value };

    public static MaterialValue OfVector2(Vector2 value) =>
        new() { Kind = MaterialValueKind.Vector2, Vector2Value = value };

    public static MaterialValue OfVector3(Vector3 value) =>
        new() { Kind = MaterialValueKind.Vector3, Vector3Value = value };

    public static MaterialValue OfVector4(Vector4 value) =>
        new() { Kind = MaterialValueKind.Vector4, Vector4Value = value };

    public static MaterialValue OfColor(Color value) => new() { Kind = MaterialValueKind.Color, ColorValue = value };

    /// <summary>A default value of the given kind — used when the editor switches a parameter's kind.</summary>
    public static MaterialValue Default(MaterialValueKind kind) => kind switch
    {
        MaterialValueKind.Bool => OfBool(false),
        MaterialValueKind.Int => OfInt(0),
        MaterialValueKind.Float => OfFloat(0f),
        MaterialValueKind.Vector2 => OfVector2(Vector2.Zero),
        MaterialValueKind.Vector3 => OfVector3(Vector3.Zero),
        MaterialValueKind.Vector4 => OfVector4(Vector4.Zero),
        MaterialValueKind.Color => OfColor(Colors.White),
        _ => new MaterialValue { Kind = MaterialValueKind.Unknown },
    };

    /// <summary>The engine wire token — a bare value, identical to what the engine sends and reads.</summary>
    internal JToken ToWire() => Kind switch
    {
        MaterialValueKind.Bool => WireValue.Write(BoolValue),
        MaterialValueKind.Int => WireValue.Write(IntValue),
        MaterialValueKind.Float => WireValue.Write(FloatValue),
        MaterialValueKind.Vector2 => WireValue.Write(Vector2Value),
        MaterialValueKind.Vector3 => WireValue.Write(Vector3Value),
        MaterialValueKind.Vector4 => WireValue.Write(Vector4Value),
        MaterialValueKind.Color => WireValue.Write(ColorValue),
        _ => Raw?.DeepClone() ?? JValue.CreateNull(),
    };

    /// <summary>Reads a bare wire token, inferring the kind from its JSON shape: a colour is a
    /// <c>{r,g,b,a}</c> object, a vector an array (by length), a scalar a number or bool; anything else
    /// is kept verbatim as <see cref="MaterialValueKind.Unknown"/>.</summary>
    internal static MaterialValue FromWire(JToken? token) => token switch
    {
        JValue { Type: JTokenType.Boolean } => OfBool(token.Value<bool>()),
        JValue { Type: JTokenType.Integer } => OfInt(token.Value<int>()),
        JValue { Type: JTokenType.Float } => OfFloat(token.Value<float>()),
        JObject => OfColor(WireValue.ReadColor(token)),
        JArray { Count: 2 } => OfVector2(WireValue.ReadVector2(token)),
        JArray { Count: 3 } => OfVector3(WireValue.ReadVector3(token)),
        JArray { Count: 4 } => OfVector4(WireValue.ReadVector4(token)),
        _ => new MaterialValue { Kind = MaterialValueKind.Unknown, Raw = token?.DeepClone() },
    };
}
