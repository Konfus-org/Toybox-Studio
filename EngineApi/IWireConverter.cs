using Newtonsoft.Json.Linq;

namespace Toybox.Studio.EngineApi;

/// <summary>
/// A custom codec between a studio type and its engine wire JSON, for members whose shape the built-in
/// <see cref="WireValue"/> codecs don't cover. Named on the member via
/// <see cref="EngineSyncAttribute.Converter"/>; the generator instantiates one per synced member. A
/// converter only ever maps the property's value — it must have a parameterless constructor and be
/// stateless; a conversion that "needs a parameter" is two named converter types. Reads are lenient: a
/// missing or malformed token yields a sensible fallback rather than throwing.
/// </summary>
public interface IWireConverter<T>
{
    /// <summary>Reads the studio value out of its wire JSON.</summary>
    T Read(JToken value);

    /// <summary>Writes the studio value as its wire JSON.</summary>
    JToken Write(T value);
}
