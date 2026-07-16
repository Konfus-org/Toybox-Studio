using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The wire codec for <see cref="MaterialParameterBindings"/>: a bare array of
/// <c>{name, data}</c> parameter entries (the element shape lives on <see cref="MaterialParameter"/>).
/// Reads are lenient — a non-array token yields an empty container.</summary>
public sealed class MaterialParameterBindingsConverter : IWireConverter<MaterialParameterBindings>
{
    public MaterialParameterBindings Read(JToken value) =>
        new() { Values = ReadParameters(value) };

    public JToken Write(MaterialParameterBindings value) => WriteParameters(value.Values);

    internal static JToken WriteParameters(IEnumerable<MaterialParameter> parameters) =>
        new JArray(parameters.Select(parameter => parameter.ToWire()));

    internal static IReadOnlyList<MaterialParameter> ReadParameters(JToken? token) =>
        token is JArray array ? [.. array.Select(MaterialParameter.FromWire)] : [];
}
