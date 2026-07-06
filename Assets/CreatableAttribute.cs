namespace Toybox.Studio.Assets;

/// <summary>
/// Declares that the engine can author a fresh default file for this asset type (a serialized body
/// asset — materials and their instances; later the world family), and the extension that file gets
/// (matching the first of the engine's <c>[[tbx::extension]]</c> tags for the type).
/// <see cref="Asset.CreateAsync"/> refuses to create anything not carrying it: imported media and
/// source-authored kinds arrive as files instead.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CreatableAttribute(string extension) : Attribute
{
    /// <summary>The file extension (no dot) a newly created asset gets.</summary>
    public string Extension { get; } = extension;
}
