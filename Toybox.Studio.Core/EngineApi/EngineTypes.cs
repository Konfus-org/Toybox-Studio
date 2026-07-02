namespace Toybox.Studio.EngineApi;

/// <summary>
/// The structural type-discriminator tokens of the engine's self-describing typed JSON — the single source of truth
/// for the value vocabulary shared by the fallback parser (<c>JsonDescriptorReader</c>), the reflected-value codec
/// (<c>EngineSyncValue</c>), and the property-widget factory. All sides hand-share this vocabulary, so they reference
/// these constants; a typo would otherwise silently misroute a value to the unknown-type placeholder.
/// </summary>
public static class EngineTypes
{
    /// <summary>A <c>std::variant</c> property; its value is the active alternative's own typed wrapper.</summary>
    public const string Variant = "variant";

    /// <summary>A resizable list (a <c>std::vector</c>).</summary>
    public const string Array = "array";

    /// <summary>A plain object / struct expanded into a sub-grid.</summary>
    public const string Object = "object";

    /// <summary>An untyped or unrecognised value.</summary>
    public const string Unknown = "unknown";

    public const string Enum = "enum";
    public const string Bool = "bool";
    public const string Int = "int";
    public const string Float = "float";
    public const string Double = "double";
    public const string String = "string";

    /// <summary>An unsigned-id scalar (edited as an integer number, not an asset picker).</summary>
    public const string Uuid = "uuid";

    public const string Vec2 = "vec2";
    public const string Vec3 = "vec3";
    public const string Vec4 = "vec4";
    public const string Mat3 = "mat3";
    public const string Mat4 = "mat4";
    public const string Quat = "quat";
    public const string Color = "color";

    /// <summary>An asset reference (routes to the asset picker; its choices are its asset-type filter).</summary>
    public const string Handle = "handle";

    /// <summary>An entity reference (routes to the entity picker).</summary>
    public const string Entity = "entity";
}
