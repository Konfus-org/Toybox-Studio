using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Utils;

/// <summary>
/// An object whose state round-trips through a JSON body — the contract the clipboard (and any other
/// body consumer) works against, so it can copy engine-mirrored objects without per-kind wrapper types.
/// Every <c>EngineObject</c> implements it via generated code: <see cref="Serialize"/> collects
/// the synced values, <see cref="Deserialize"/> applies a body back.
/// </summary>
public interface ISerializable
{
    /// <summary>The object's synced state as a wire-shaped JSON body (one entry per synced property).</summary>
    JObject Serialize();

    /// <summary>Applies every recognized entry of <paramref name="body"/> to the object's local state.</summary>
    void Deserialize(JObject body);
}
