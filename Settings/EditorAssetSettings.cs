using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using Toybox.Studio.Utils.Attributes;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Settings;

/// <summary>
/// How <c>File ▸ Open ▸ Asset</c> opens a chosen asset into an Asset Viewer viewport.
/// </summary>
public enum OpenAssetsMode
{
    /// <summary>Every opened asset spawns its own fresh Asset Viewer viewport.</summary>
    [DisplayName("New Viewport")]
    [Tooltip("Open each asset in its own new Asset Viewer viewport.")]
    NewViewport,

    /// <summary>An opened asset loads into the already-open Asset Viewer, if there is one.</summary>
    [DisplayName("Reuse Viewport")]
    [Tooltip("Load assets into the existing Asset Viewer if one is open; otherwise open a new one.")]
    ReuseViewport,
}

/// <summary>
/// Editor-side asset preferences: how assets open into the viewport, and the Asset Browser's category
/// layout. Plain data — <see cref="EditorSettings"/> owns it, and it persists to EditorSettings.json
/// like every other section. (The browser panel that consumes <see cref="Categories"/> is a later step;
/// for now that only defines the shape.)
/// </summary>
[Icon("FolderTree")]
public sealed class EditorAssetSettings
{
    /// <summary>
    /// Whether opening an asset reuses an already-open Asset Viewer viewport or spawns a new one each
    /// time. Persisted by name so the settings file stays hand-readable.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public OpenAssetsMode OpenAssetsMode { get; set; } = OpenAssetsMode.ReuseViewport;

    /// <summary>The categories, in the order the browser lays them out.</summary>
    public List<AssetBrowserCategory> Categories { get; set; } = DefaultCategories();

    // A reasonable out-of-the-box layout, each category carrying the icon + accent colour the browser tints
    // its tiles with. Mostly extension filters; Scripts also keys off the .meta "script" type (matched on
    // the row's script flag) and folds in clang config files, and CMake/Documentation group the project's
    // non-asset support files (which never carry a .meta, so the browser doesn't flag them for one).
    private static List<AssetBrowserCategory> DefaultCategories() =>
    [
        new() { Name = "Models", FileNamePattern = new(@"\.(fbx|gltf|glb|obj|dae)$"),
            Icon = CategoryIcon.Model, Color = CategoryColor.Blue },
        new() { Name = "Blender", FileNamePattern = new(@"\.blend$"),
            Icon = CategoryIcon.Blender, Color = CategoryColor.Orange },
        new() { Name = "Textures", FileNamePattern = new(@"\.(png|jpe?g|tga|dds|hdr)$"),
            Icon = CategoryIcon.Texture, Color = CategoryColor.Green },
        new() { Name = "Materials", FileNamePattern = new(@"\.(mat|mti)$"),
            Icon = CategoryIcon.Material, Color = CategoryColor.Magenta },
        new() { Name = "Shaders", FileNamePattern = new(@"\.(glsl|vert|frag|comp|hlsl)$"),
            Icon = CategoryIcon.Shader, Color = CategoryColor.Violet },
        new() { Name = "Audio", FileNamePattern = new(@"\.(wav|ogg|mp3|flac)$"),
            Icon = CategoryIcon.Audio, Color = CategoryColor.Amber },
        new() { Name = "Scripts", FileNamePattern = new(@"\.clang-(format|tidy)$"),
            MetaPattern = new("\"type\"\\s*:\\s*\"script\""),
            Icon = CategoryIcon.Script, Color = CategoryColor.Teal },
        new() { Name = "CMake",
            FileNamePattern = new(@"(CMakeLists\.txt|CMakePresets\.json|CMakeCache\.txt|compile_commands\.json|\.cmake)$"),
            Icon = CategoryIcon.Config, Color = CategoryColor.Gray },
        new() { Name = "Documentation", FileNamePattern = new(@"((README|LICENSE|CHANGELOG)[^/]*$|\.md$)"),
            Icon = CategoryIcon.Document, Color = CategoryColor.Cyan },
    ];
}

/// <summary>
/// One Asset Browser category: a display name plus the two patterns that claim assets for it —
/// <see cref="FileNamePattern"/> tested against the asset's file name and <see cref="MetaPattern"/>
/// against its <c>.meta</c> sidecar text. Either may be left empty (an empty pattern matches nothing),
/// so a category can key off the extension, off meta content, or both. The <see cref="Name"/> labels
/// the category's row in the settings grid.
/// </summary>
public sealed class AssetBrowserCategory
{
    /// <summary>The category's display name — also its row label in the settings grid.</summary>
    public string Name { get; set; } = "Category";

    /// <summary>The icon the browser tags this category's assets with (mapped to a concrete glyph there).</summary>
    [Icon("Image")]
    [JsonConverter(typeof(StringEnumConverter))]
    public CategoryIcon Icon { get; set; } = CategoryIcon.File;

    /// <summary>The accent colour the browser tints this category's asset icons with.</summary>
    [Icon("Palette")]
    [JsonConverter(typeof(StringEnumConverter))]
    public CategoryColor Color { get; set; } = CategoryColor.Default;

    /// <summary>Regex matched against an asset's file name — the usual extension filter.</summary>
    [Icon("FileType")]
    public RegexPattern FileNamePattern { get; set; }

    /// <summary>Regex matched against the asset's <c>.meta</c> sidecar text — sorts by anything the
    /// meta records (type info, tags, …) rather than the file name.</summary>
    [Icon("Tags")]
    public RegexPattern MetaPattern { get; set; }
}
