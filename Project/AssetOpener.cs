using System;
using Toybox.Studio.Dialogs;
using Toybox.Studio.Logging;
using Toybox.Studio.Scripting;
using Toybox.Studio.Worlds;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Project;

/// <summary>
/// Routes "open this asset" to the surface that fits its type: a C++ script opens its .h/.cpp source pair in
/// the Monaco code editor (never its .meta), shaders open the source file there; worlds (and their globals/chunk
/// parts) switch the active editing world; 3D models, materials, material instances (.mti) and textures open in
/// a new Asset Viewer (an isolated orbit preview); anything else is handed to the OS's default program.
/// Shared by the menu-bar "Open Asset" picker and the Asset Browser so both route identically. Open operations
/// touch dockables, so call <see cref="OpenAsync"/> on the UI thread.
/// </summary>
public sealed class AssetOpener
{
    // Source extensions that open in the text editor alongside C++ scripts.
    private static readonly HashSet<string> ShaderTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "glsl", "hlsl", "wgsl", "vert", "frag", "vertex", "fragment", "geom", "geometry",
        "comp", "compute", "tesc", "tese", "vsh", "fsh",
    };

    // Types that preview in the Asset Viewer's orbit view. A material instance (.mti) previews like a material.
    private static readonly HashSet<string> PreviewableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbx", "obj", "gltf", "glb", "dae", "mesh",
        "png", "jpg", "jpeg", "bmp", "tga", "dds", "hdr", "exr", "ktx", "ktx2",
        "mat", "mti",
    };

    // Asset types that, opened, switch the active editing world (the world file and its globals/chunk parts).
    private static readonly HashSet<string> WorldTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "world", "globals", "chunk",
    };

    private readonly ProjectManager _projects;
    private readonly ScriptEditorLauncher _scriptEditor;
    private readonly ScriptEditing _scriptEditing;
    private readonly AssetViewerLauncher _assetViewer;
    private readonly GameState _world;
    private readonly Logger _log;

    public AssetOpener(
        ProjectManager projects, ScriptEditorLauncher scriptEditor, ScriptEditing scriptEditing,
        AssetViewerLauncher assetViewer, GameState world, Logger log)
    {
        _projects = projects;
        _scriptEditor = scriptEditor;
        _scriptEditing = scriptEditing;
        _assetViewer = assetViewer;
        _world = world;
        _log = log;
    }

    /// <summary>
    /// Opens the asset on the surface that fits its type (see the class summary). Call on the UI thread. When
    /// <paramref name="newWindow"/> is true a previewable asset opens in a brand-new Asset Viewer; otherwise it
    /// reuses the open one. The flag only affects previewable types — scripts always go to the (singleton) code
    /// editor, worlds to the active world, and everything else to the OS default program.
    /// </summary>
    public async Task OpenAsync(AssetMeta asset, bool newWindow = false)
    {
        // A C++ script's asset is its `.h.meta` sidecar; open the real `.h` + `.cpp` pair (exactly like the
        // inspector's pop-out), never the meta file itself.
        if (asset.IsScript)
        {
            if (_projects.CurrentProject is { } project)
                _scriptEditing.PopOut(AssetPairing.StripMetadata(ResolveAssetPath(project, asset.Path)));
            else
                _log.Error("Open a project before opening a script.");
            return;
        }

        // A shader source is a plain text file — open it directly in the code editor.
        if (ShaderTypes.Contains(asset.Type))
        {
            if (_projects.CurrentProject is { } project)
                _scriptEditor.Open(ResolveAssetPath(project, asset.Path));
            else
                _log.Error("Open a project before opening a shader.");
            return;
        }

        // A world (or one of its globals/chunk parts) becomes the active editing world rather than a preview.
        if (WorldTypes.Contains(asset.Type))
        {
            await _world
                .OpenWorldAsync(new AssetHandle(asset.Id, asset.Name, asset.Type, asset.Path))
                .ContinueOnAnyContext();
            return;
        }

        // Models, materials and textures get the live orbit preview — reusing the open viewer by default.
        if (PreviewableTypes.Contains(asset.Type))
        {
            if (newWindow)
                _assetViewer.OpenInNewWindow(asset);
            else
                _assetViewer.Open(asset);
            return;
        }

        // Anything we don't have a dedicated surface for: let the OS open it with its default program.
        OpenWithDefaultApp(asset);
    }

    private void OpenWithDefaultApp(AssetMeta asset)
    {
        if (_projects.CurrentProject is not { } project)
        {
            _log.Error("Open a project before opening an asset.");
            return;
        }

        FileOpen.WithDefaultApp(ResolveAssetPath(project, asset.Path));
    }

    // Resolves a project-relative asset path to an absolute one (under the project root, else its Assets
    // folder), passing an already-rooted path through. Mirrors App/ShellViewModel's resolver.
    private static string ResolveAssetPath(ProjectInfo project, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return project.AssetsDirectory;

        if (Path.IsPathRooted(relativePath))
            return relativePath;

        var underRoot = Path.Combine(project.RootDirectory, relativePath);
        if (File.Exists(underRoot))
            return underRoot;

        var underAssets = Path.Combine(project.AssetsDirectory, relativePath);
        return File.Exists(underAssets) ? underAssets : underRoot;
    }
}
