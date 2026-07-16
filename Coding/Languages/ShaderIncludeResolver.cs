namespace Toybox.Studio.Coding.Languages;

/// <summary>
/// Resolves a GLSL <c>#include</c> spec (as written at the top of a shader) to a real file, mirroring how the
/// engine's shader loader resolves them: relative to the including file, then under the project's and engine's
/// shader roots. This is the host half of GLSL editor support — the page's include provider asks this to turn
/// <c>#include "Globals.glsl"</c> into a path it can navigate to. Highlight-only otherwise (no language server).
/// </summary>
public sealed class ShaderIncludeResolver
{
    private readonly string _projectRoot;
    private readonly string? _engineRoot;

    public ShaderIncludeResolver(string projectRoot, string? engineRoot)
    {
        _projectRoot = projectRoot;
        _engineRoot = engineRoot;
    }

    /// <summary>
    /// The absolute path the include resolves to, or null when it can't be found. <paramref name="fromPath"/>
    /// is the shader doing the include; <paramref name="spec"/> is the include target (quotes/brackets already
    /// stripped, e.g. <c>Globals.glsl</c> or <c>lighting/pbr.glsl</c>).
    /// </summary>
    public string? Resolve(string fromPath, string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return null;

        var relative = spec.Trim().Replace('\\', '/');
        foreach (var root in SearchRoots(fromPath))
        {
            var candidate = Path.GetFullPath(Path.Combine(root, relative));
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    // The directories an include is looked up in, in priority order: the including file's own folder, then the
    // project's and engine's asset/shader roots (matching the engine loader's search set).
    private IEnumerable<string> SearchRoots(string fromPath)
    {
        var fromDir = Path.GetDirectoryName(Path.GetFullPath(fromPath));
        if (!string.IsNullOrEmpty(fromDir))
            yield return fromDir;

        if (!string.IsNullOrEmpty(_projectRoot))
        {
            yield return Path.Combine(_projectRoot, "Assets", "Shaders");
            yield return Path.Combine(_projectRoot, "Assets");
        }

        if (!string.IsNullOrEmpty(_engineRoot))
        {
            yield return Path.Combine(_engineRoot, "resources", "Shaders");
            yield return Path.Combine(_engineRoot, "resources");
        }
    }
}
