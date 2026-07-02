using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Toybox.Studio.Utils;

namespace Toybox.Studio.Settings;

/// <summary>
/// A user-defined Asset Browser category: a display name, a glyph, and the file types it collects.
/// <see cref="Extensions"/> is a comma/space separated list of match tokens — usually bare file extensions
/// ("fbx", "png"), plus two special forms the browser understands: <c>mat:&lt;role&gt;</c> matches a material
/// by its render role (e.g. <c>mat:sky</c> for sky materials, so skies can be split from ordinary materials)
/// and <c>name:cmake</c> matches files named like <c>CMakeLists</c>. Matching is case-insensitive, and a C++
/// script (whose file is a <c>.h.meta</c> sidecar) matches on its source extension (e.g. "h"). <see cref="Icon"/>
/// is a Lucide icon (persisted as its name); <see cref="Color"/> is a <see cref="PaletteColor"/> token (persisted
/// as its name), resolved to a colour via <c>Colors.ToColor</c> when a tile/rail is rendered.
/// </summary>
public sealed class AssetCategory
{
    public AssetCategory()
    {
    }

    public AssetCategory(string name, Icon icon, PaletteColor color, string extensions)
    {
        Name = name;
        Icon = icon;
        Color = color;
        Extensions = extensions;
    }

    public string Name { get; set; } = string.Empty;

    [JsonConverter(typeof(IconJsonConverter))]
    public Icon Icon { get; set; } = Icon.File;

    [JsonConverter(typeof(StringEnumConverter))]
    public PaletteColor Color { get; set; } = PaletteColor.Grey;

    public string Extensions { get; set; } = string.Empty;

    /// <summary>The built-in starter categories, used when the user hasn't customised the list.</summary>
    public static List<AssetCategory> Defaults() =>
    [
        new("Models", Icon.Box, PaletteColor.Blue, "fbx, obj, gltf, glb, dae, mesh"),
        new("Textures", Icon.Image, PaletteColor.Cyan, "png, jpg, jpeg, bmp, tga, dds, hdr, exr, ktx, ktx2"),
        new("Skies", Icon.Cloud, PaletteColor.Blue, "mat:sky"),
        new("Materials", Icon.Palette, PaletteColor.Yellow, "mat, mti"),
        new("Shaders", Icon.Sparkles, PaletteColor.Magenta, "glsl, hlsl, wgsl, vert, frag, comp, geom, tesc, tese, vsh, fsh, spv, shader"),
        new("Blender", Icon.Box, PaletteColor.Yellow, "blend"),
        new("Audio", Icon.Music, PaletteColor.Green, "wav, mp3, ogg, flac"),
        new("Scripts", Icon.Code, PaletteColor.Blue, "h, hpp, hh, hxx, cpp, cxx, cc, c, cs, lua, py"),
        new("Worlds", Icon.Globe, PaletteColor.Cyan, "world, globals, chunk"),
        new("CMake", Icon.Wrench, PaletteColor.Grey, "cmake, name:cmake"),
    ];

    /// <summary>
    /// Folds each built-in category's tokens back into the matching (by name) user category, so a list persisted
    /// before a token was added still groups those assets correctly — e.g. material instances (<c>mti</c>) under
    /// Materials and chunk/globals world parts under Worlds. User-added tokens and custom categories are kept;
    /// only missing built-in tokens are added. Returns a new list (the persisted settings are not mutated).
    /// </summary>
    public static List<AssetCategory> MergedWithDefaults(IReadOnlyList<AssetCategory> categories)
    {
        var defaultsByName = Defaults().ToDictionary(category => category.Name, StringComparer.OrdinalIgnoreCase);
        var merged = new List<AssetCategory>(categories.Count);
        foreach (var category in categories)
        {
            if (!defaultsByName.TryGetValue(category.Name, out var builtin))
            {
                merged.Add(category);
                continue;
            }

            var tokens = Tokenize(category.Extensions);
            foreach (var token in Tokenize(builtin.Extensions))
                if (!tokens.Contains(token))
                    tokens.Add(token);

            merged.Add(new AssetCategory(category.Name, category.Icon, category.Color, string.Join(", ", tokens)));
        }

        return merged;
    }

    // The extension/match tokens of a CSV list, trimmed and de-duplicated while preserving order.
    private static List<string> Tokenize(string extensions) =>
        extensions
            .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
