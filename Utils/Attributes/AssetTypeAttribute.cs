namespace Toybox.Studio.Utils.Attributes;

/// <summary>
/// Constrains an asset-handle property to assets of the given file-extension tokens ("mat", "png", …),
/// so a reflective asset picker lists only matching assets. Mirrors the engine's <c>[[tbx::asset(…)]]</c>
/// filter. A handle property without it accepts any asset. Lives in Utils so plain data/asset layers can
/// tag members without referencing any UI project.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AssetTypeAttribute(params string[] extensions) : Attribute
{
    /// <summary>The accepted file-extension tokens, without the dot.</summary>
    public IReadOnlyList<string> Extensions { get; } = extensions;
}
