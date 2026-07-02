using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Project.Assets;

/// <summary>
/// Serialized mirror of the engine's <c>MaterialParameter</c> — one shader parameter keyed by binding name. The
/// engine value is a tagged variant (bool/int/float/double/vecN/colour/matN), so <see cref="Data"/> is kept as the
/// raw engine token and round-trips verbatim; the material editors interpret it.
/// </summary>
public sealed class MaterialParameter
{
    public string Name { get; set; } = "";

    public JToken Data { get; set; } = JValue.CreateNull();
}
