using Toybox.Studio.EngineApi.Types;
namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// A material's parameter bindings, mirroring the engine's <c>MaterialParameterBindings</c>. Queries
/// match the C++ surface; the edits (<see cref="Set"/>/<see cref="Remove"/>/<see cref="Clear"/>)
/// return a new bindings value — assign it back to the owning asset's property, which is what pushes
/// the change to the engine.
/// </summary>
public sealed record MaterialParameterBindings
{
    public IReadOnlyList<MaterialParameter> Values { get; init; } = [];

    public MaterialParameter? Get(string name) =>
        Values.FirstOrDefault(parameter => parameter.Name == name);

    public bool Has(string name) => Get(name) is not null;

    /// <summary>These bindings with <paramref name="parameter"/> replaced in place (or appended).</summary>
    public MaterialParameterBindings Set(MaterialParameter parameter)
    {
        var values = new List<MaterialParameter>(Values.Count + 1);
        var replaced = false;
        foreach (var existing in Values)
        {
            values.Add(existing.Name == parameter.Name ? parameter : existing);
            replaced |= existing.Name == parameter.Name;
        }

        if (!replaced)
            values.Add(parameter);
        return new MaterialParameterBindings { Values = values };
    }

    /// <summary>These bindings without the named parameter.</summary>
    public MaterialParameterBindings Remove(string name) =>
        new() { Values = [.. Values.Where(parameter => parameter.Name != name)] };

    /// <summary>Empty bindings.</summary>
    public MaterialParameterBindings Clear() => new();
}
