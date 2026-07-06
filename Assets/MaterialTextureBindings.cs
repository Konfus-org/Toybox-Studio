using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Assets;

/// <summary>
/// A material's texture bindings, mirroring the engine's <c>MaterialTextureBindings</c>. Queries match
/// the C++ surface; the edits (<see cref="Set"/>/<see cref="Remove"/>/<see cref="Clear"/>) return a new
/// bindings value — assign it back to the owning asset's property, which is what pushes the change to
/// the engine.
/// </summary>
public sealed record MaterialTextureBindings
{
    public IReadOnlyList<MaterialTextureBinding> Values { get; init; } = [];

    public MaterialTextureBinding? Get(string name) =>
        Values.FirstOrDefault(binding => binding.Name == name);

    public bool Has(string name) => Get(name) is not null;

    /// <summary>These bindings with the named texture set (replaced in place, or appended).</summary>
    public MaterialTextureBindings Set(string name, Handle texture) =>
        Set(new MaterialTextureBinding { Name = name, Texture = texture });

    /// <summary>These bindings with <paramref name="binding"/> replaced in place (or appended).</summary>
    public MaterialTextureBindings Set(MaterialTextureBinding binding)
    {
        var values = new List<MaterialTextureBinding>(Values.Count + 1);
        var replaced = false;
        foreach (var existing in Values)
        {
            values.Add(existing.Name == binding.Name ? binding : existing);
            replaced |= existing.Name == binding.Name;
        }

        if (!replaced)
            values.Add(binding);
        return new MaterialTextureBindings { Values = values };
    }

    /// <summary>These bindings without the named texture.</summary>
    public MaterialTextureBindings Remove(string name) =>
        new() { Values = [.. Values.Where(binding => binding.Name != name)] };

    /// <summary>Empty bindings.</summary>
    public MaterialTextureBindings Clear() => new();
}
