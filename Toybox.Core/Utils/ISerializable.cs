using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Utils;

/// <summary>
/// A JSON round-trip contract: an object that can write its whole state to a <see cref="JObject"/> body and apply
/// one back onto a live instance. It lets the <c>Clipboard</c> (and any other generic copy/restore) move an
/// object's state around without knowing its concrete type — the copy stores <see cref="Serialize"/>'s body and a
/// later paste hands that body to <see cref="Deserialize"/> on the destination. Implemented by the engine-synced
/// objects (entities, components), whose serialized form is the engine's describe JSON.
/// </summary>
public interface ISerializable
{
    /// <summary>This object's whole state as a JSON body — the input a later <see cref="Deserialize"/> restores.</summary>
    JObject Serialize();

    /// <summary>Applies a serialized body (from <see cref="Serialize"/>) back onto this object, reporting the
    /// outcome; a paste onto an engine-backed object pushes the restored state to the engine.</summary>
    Task<Result> Deserialize(JObject body, CancellationToken ct = default);
}
