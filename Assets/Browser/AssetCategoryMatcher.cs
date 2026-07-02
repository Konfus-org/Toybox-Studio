using System.IO;
using System.Linq;
using Toybox.Studio.Project;
using Toybox.Studio.Settings;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// Maps catalog assets onto the user's configured <see cref="AssetCategory"/> list and decides what the browser
/// hides. Matching is token-based: an asset yields a few tokens — its (effective) file extension, a material's
/// <c>mat:&lt;role&gt;</c>, and a <c>name:cmake</c> marker — and the first category whose extensions contain any
/// token claims it. Unmatched assets fall into the browser's "Other" bucket.
/// </summary>
public static class AssetCategoryMatcher
{
    private static readonly HashSet<string> CppSourceTypes = new(StringComparer.OrdinalIgnoreCase)
    { "h", "hpp", "hh", "hxx", "cpp", "cxx", "cc", "c", "ipp", "inl" };

    private static readonly HashSet<string> ModelTypes = new(StringComparer.OrdinalIgnoreCase)
    { "fbx", "obj", "gltf", "glb", "dae", "mesh" };

    private static readonly HashSet<string> MaterialTypes = new(StringComparer.OrdinalIgnoreCase)
    { "mat", "mti" };

    private static readonly HashSet<string> TextureTypes = new(StringComparer.OrdinalIgnoreCase)
    { "png", "jpg", "jpeg", "bmp", "tga", "dds", "hdr", "exr", "ktx", "ktx2" };

    private static readonly char[] ExtensionSeparators = [',', ' ', ';'];

    /// <summary>Whether a file extension is a 3D model — the single definition of the model set, shared by the
    /// browser and the asset viewer so the two can't drift.</summary>
    public static bool IsModelType(string type) => ModelTypes.Contains(type);

    /// <summary>Whether a file extension is a material — a <c>.mat</c> or a <c>.mti</c> (material instance).</summary>
    public static bool IsMaterialType(string type) => MaterialTypes.Contains(type);

    /// <summary>Whether a file extension is a texture.</summary>
    public static bool IsTextureType(string type) => TextureTypes.Contains(type);

    /// <summary>Whether the asset is a 3D model — the only kind with measurable geometry for the hover HUD's
    /// engine-unit scale (a fixed extension test, independent of the user's display categories).</summary>
    public static bool IsModel(AssetMeta asset) => IsModelType(asset.Type);

    /// <summary>Whether the asset previews in the 3D viewer — a model, a material (.mat or .mti), or a texture.
    /// These get the live hover preview; everything else (scripts, worlds, …) does not.</summary>
    public static bool IsPreviewable(AssetMeta asset) =>
        IsModelType(asset.Type) || IsMaterialType(asset.Type) || IsTextureType(asset.Type);

    /// <summary>
    /// Whether an asset is bookkeeping the browser should not show: plain <c>.meta</c> sidecars (a script's
    /// self-describing <c>.h.meta</c> is the asset itself, so it is kept), and dotfile tooling configs such as
    /// <c>.clang-format</c> / <c>.clang-tidy</c> that aren't content.
    /// </summary>
    public static bool IsHidden(AssetMeta asset)
    {
        if (asset.Name.StartsWith('.'))
            return true;

        // Files at the project root (no folder in their project-relative path) are project scaffolding —
        // CMakeLists.txt, AppSettings.json, compile_commands.json, … — not content. Real assets live under a
        // folder (Assets/, Source/, …), so a path with no separator is a root file: hide it.
        var path = asset.Path;
        if (path.Length > 0 && path.IndexOf('/') < 0 && path.IndexOf('\\') < 0)
            return true;

        // Anything under a build-output folder is generated, never source content.
        if (path.Split('/', '\\').Any(segment => segment.Equals("build", StringComparison.OrdinalIgnoreCase)))
            return true;

        return string.Equals(asset.Type, "meta", StringComparison.OrdinalIgnoreCase) && !asset.IsScript;
    }

    /// <summary>
    /// Whether an asset is part of the open project's own content tree — what the browser should show. The engine
    /// reports every registered asset across all roots (the project, the engine's shared resources, the editor's
    /// asset-viewer built-ins), as absolute, forward-slashed paths. Project content is an asset that lives in a
    /// <em>subfolder</em> under the project root: this drops the project's own top-level files (AppSettings.json,
    /// CMakePresets.json, compile_commands.json, …) and everything outside the project (engine resources, the
    /// asset-viewer set). The dropped assets stay in the catalog so pickers/handles still resolve them by id.
    /// Comparison normalises slashes and is case-insensitive (Windows paths). An unknown project root keeps the
    /// asset (the catalog is empty without a connected project anyway).
    /// </summary>
    public static bool IsProjectContent(AssetMeta asset, string projectRoot)
    {
        var root = projectRoot.Replace('\\', '/').TrimEnd('/');
        if (root.Length == 0)
            return true;

        var path = asset.Path.Replace('\\', '/');
        if (!path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            return false;

        // A top-level project file has no further separator after the root; real content sits in a subfolder.
        return path.IndexOf('/', root.Length + 1) >= 0;
    }

    /// <summary>
    /// The type-pill text for an asset. A C++ script's underlying file is a <c>.h.meta</c> sidecar (so its raw
    /// type is "meta"); show it by its source language — "C++" for a C/C++ source, otherwise the source
    /// extension. Everything else uses its upper-cased extension.
    /// </summary>
    public static string TypeLabel(AssetMeta asset)
    {
        if (!asset.IsScript)
            return asset.Type.ToUpperInvariant();

        var extension = Path.GetExtension(asset.Name).TrimStart('.');
        if (extension.Length == 0 || CppSourceTypes.Contains(extension))
            return "C++";

        return extension.ToUpperInvariant();
    }

    /// <summary>The first category that claims this asset, or null when none do (the "Other" bucket).</summary>
    public static AssetCategory? Match(IReadOnlyList<AssetCategory> categories, AssetMeta asset)
    {
        var tokens = Tokens(asset);
        foreach (var category in categories)
        {
            if (ParseExtensions(category.Extensions).Any(tokens.Contains))
                return category;
        }

        return null;
    }

    private static HashSet<string> Tokens(AssetMeta asset)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var extension = EffectiveExtension(asset);
        if (extension.Length > 0)
            tokens.Add(extension);

        if (string.Equals(asset.Type, "mat", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(asset.MaterialType))
            tokens.Add("mat:" + asset.MaterialType.ToLowerInvariant());

        if (asset.Name.StartsWith("CMake", StringComparison.OrdinalIgnoreCase))
            tokens.Add("name:cmake");

        return tokens;
    }

    // A C++ script's file is a .h.meta sidecar, so match it on its source extension (read off the display
    // name, e.g. "Player.h" → "h") rather than the "meta" file type.
    private static string EffectiveExtension(AssetMeta asset)
    {
        var raw = asset.IsScript ? Path.GetExtension(asset.Name) : "." + asset.Type;
        return raw.TrimStart('.').ToLowerInvariant();
    }

    private static IEnumerable<string> ParseExtensions(string csv) =>
        csv.Split(ExtensionSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.ToLowerInvariant());
}
