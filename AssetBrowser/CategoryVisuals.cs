using Avalonia.Media.Immutable;
using Avalonia.Media;
using Toybox.Studio.Settings;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// Maps a category's editor-defined <see cref="CategoryIcon"/> / <see cref="CategoryColor"/> to the
/// concrete Lucide glyph and accent brush the browser renders. This is the one place the UI-free settings
/// enums become UI values, so tiles and the rail stay in step.
/// </summary>
internal static class CategoryVisuals
{
    private static readonly IReadOnlyDictionary<CategoryColor, IBrush> Brushes = BuildBrushes();

    public static Icon IconFor(CategoryIcon icon) => icon switch
    {
        CategoryIcon.Folder => Icon.Folder,
        CategoryIcon.Model => Icon.Box,
        CategoryIcon.Blender => Icon.Blend,
        CategoryIcon.Texture => Icon.Image,
        CategoryIcon.Material => Icon.Palette,
        CategoryIcon.Shader => Icon.Sparkles,
        CategoryIcon.Audio => Icon.Music,
        CategoryIcon.Script => Icon.Code,
        CategoryIcon.Document => Icon.FileText,
        CategoryIcon.Config => Icon.Wrench,
        CategoryIcon.Input => Icon.Gamepad2,
        CategoryIcon.World => Icon.Globe,
        CategoryIcon.Settings => Icon.Cog,
        _ => Icon.File,
    };

    public static IBrush BrushFor(CategoryColor color) =>
        Brushes.TryGetValue(color, out var brush) ? brush : Brushes[CategoryColor.Default];

    private static IReadOnlyDictionary<CategoryColor, IBrush> BuildBrushes() => new Dictionary<CategoryColor, IBrush>
    {
        [CategoryColor.Default] = Solid("#9CA3AF"),
        [CategoryColor.Gray] = Solid("#9CA3AF"),
        [CategoryColor.Red] = Solid("#EF4444"),
        [CategoryColor.Orange] = Solid("#F97316"),
        [CategoryColor.Amber] = Solid("#F59E0B"),
        [CategoryColor.Yellow] = Solid("#EAB308"),
        [CategoryColor.Green] = Solid("#22C55E"),
        [CategoryColor.Teal] = Solid("#14B8A6"),
        [CategoryColor.Cyan] = Solid("#06B6D4"),
        [CategoryColor.Blue] = Solid("#3B82F6"),
        [CategoryColor.Indigo] = Solid("#6366F1"),
        [CategoryColor.Violet] = Solid("#8B5CF6"),
        [CategoryColor.Magenta] = Solid("#D946EF"),
        [CategoryColor.Pink] = Solid("#EC4899"),
    };

    private static IBrush Solid(string hex) => new ImmutableSolidColorBrush(Color.Parse(hex));
}
