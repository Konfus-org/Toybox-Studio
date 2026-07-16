using Toybox.Studio.EngineApi.Types;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.EngineApi.Types.Assets;

/// <summary>
/// The per-instance parameter and texture overrides layered onto a base material, mirroring the
/// engine's <c>MaterialOverrides</c>. An override is "present" exactly when its list is non-empty —
/// derived, never stored. Render config is not overridable per-instance; it lives on the base
/// <see cref="Material"/> and is shared by every instance.
/// </summary>
public sealed record MaterialOverrides
{
    public IReadOnlyList<MaterialTextureBinding> Textures { get; init; } = [];

    public IReadOnlyList<MaterialParameter> Parameters { get; init; } = [];

    public bool HasTextureOverride => Textures.Count > 0;

    public bool HasParameterOverride => Parameters.Count > 0;

    public MaterialParameter? GetParameter(string name) =>
        Parameters.FirstOrDefault(parameter => parameter.Name == name);

    public MaterialTextureBinding? GetTexture(string name) =>
        Textures.FirstOrDefault(binding => binding.Name == name);

    /// <summary>These overrides with <paramref name="parameter"/> replaced in place (or appended).</summary>
    public MaterialOverrides SetParameter(MaterialParameter parameter)
    {
        var values = new List<MaterialParameter>(Parameters.Count + 1);
        var replaced = false;
        foreach (var existing in Parameters)
        {
            values.Add(existing.Name == parameter.Name ? parameter : existing);
            replaced |= existing.Name == parameter.Name;
        }

        if (!replaced)
            values.Add(parameter);
        return this with { Parameters = values };
    }

    /// <summary>These overrides with the named texture binding set (replaced in place, or appended).</summary>
    public MaterialOverrides SetTexture(string name, Handle texture)
    {
        var binding = new MaterialTextureBinding { Name = name, Texture = texture };
        var values = new List<MaterialTextureBinding>(Textures.Count + 1);
        var replaced = false;
        foreach (var existing in Textures)
        {
            values.Add(existing.Name == name ? binding : existing);
            replaced |= existing.Name == name;
        }

        if (!replaced)
            values.Add(binding);
        return this with { Textures = values };
    }
}
