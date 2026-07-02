using Newtonsoft.Json.Linq;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// Custom (de)serialization between a typed reflected field and its engine wire value — the escape hatch a
/// <see cref="EngineSyncAttribute"/> names via its converter for a type the default generic path can't handle
/// (a binary blob, or a shape that differs from the field's natural serialization). Stateless; the generator news
/// one up per use.
/// </summary>
public interface IEngineSyncConverter<T>
{
    /// <summary>Reads the engine's bare wire value into the typed field value.</summary>
    T Read(JToken value);

    /// <summary>Writes the typed field value to its bare engine wire value.</summary>
    JToken Write(T value);
}
