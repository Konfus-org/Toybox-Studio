using System;
using System.Collections.Generic;

namespace Toybox.Studio.Project;

/// <summary>
/// Restricts an asset-handle member's picker to the given file extensions — the C# counterpart of the engine's
/// <c>[[tbx::asset(...)]]</c>. Drives the handle picker's filter (the grid node's <c>Choices</c>). E.g.
/// <c>[AssetExtensions("fbx", "obj", "gltf", "glb")]</c> on a model handle.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AssetExtensionsAttribute(params string[] extensions) : Attribute
{
    public IReadOnlyList<string> Extensions { get; } = extensions;
}
