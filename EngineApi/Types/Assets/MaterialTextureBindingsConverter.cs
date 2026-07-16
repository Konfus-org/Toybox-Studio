using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>The wire codec for <see cref="MaterialTextureBindings"/>: a bare array of
/// <c>{name, texture}</c> binding entries (the element shape lives on
/// <see cref="MaterialTextureBinding"/>). Reads are lenient — a non-array token yields an empty
/// container.</summary>
public sealed class MaterialTextureBindingsConverter : IWireConverter<MaterialTextureBindings>
{
    public MaterialTextureBindings Read(JToken value) =>
        new() { Values = ReadBindings(value) };

    public JToken Write(MaterialTextureBindings value) => WriteBindings(value.Values);

    internal static JToken WriteBindings(IEnumerable<MaterialTextureBinding> bindings) =>
        new JArray(bindings.Select(binding => binding.ToWire()));

    internal static IReadOnlyList<MaterialTextureBinding> ReadBindings(JToken? token) =>
        token is JArray array ? [.. array.Select(MaterialTextureBinding.FromWire)] : [];
}
