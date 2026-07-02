namespace Toybox.Studio.EngineApi;

/// <summary>
/// The JSON keys of the engine's self-describing typed-value envelope — the single source of truth shared by the
/// fallback parser (<c>JsonDescriptorReader</c>, which reads engine-authored describe bodies) and the reflected-value
/// codec (<see cref="EngineSyncValue"/>/<see cref="EngineSyncedObject"/>, which serialises Studio's typed models to
/// and from the wire). Both sides MUST agree byte-for-byte, so they reference these constants rather than
/// re-spelling the literals. Mirrors the engine's <c>PROPERTY_*_KEY</c> (serialization.h).
/// </summary>
public static class EngineKeys
{
    /// <summary>The structural type token of a value (see <see cref="EngineTypes"/>).</summary>
    public const string Type = "type";

    /// <summary>The value payload of a typed wrapper.</summary>
    public const string Value = "value";

    /// <summary>The unwrapped semantic type name (e.g. <c>quat</c> for a rotation carried under the <c>vec4</c>
    /// structural token) that disambiguates a leaf sharing a structural token.</summary>
    public const string Nested = "nested";

    /// <summary>The describe path's metadata sub-object holding <see cref="Type"/> and the editor metadata.</summary>
    public const string Attributes = "attributes";

    public const string Category = "category";
    public const string Description = "description";

    /// <summary>Enum options, or a reference type's asset-type filter.</summary>
    public const string Choices = "choices";

    /// <summary>The custom view-model type name a value routes its editor to.</summary>
    public const string View = "view";

    public const string Label = "label";
    public const string ReadOnly = "readonly";
    public const string Hidden = "hidden";

    /// <summary>Whether the value currently equals its default (engine-authoritative; describe-only).</summary>
    public const string IsDefault = "is_default";

    /// <summary>An inlined default value the describe carries (a script binding's override fields).</summary>
    public const string Default = "default";

    public const string Order = "order";

    /// <summary>A resizable list's default element JSON, cloned by the list widget to append.</summary>
    public const string ElementTemplate = "element_template";
}
