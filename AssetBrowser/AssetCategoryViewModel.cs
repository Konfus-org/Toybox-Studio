using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.Settings;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// One entry in the browser's category rail: the special <b>All</b> (every asset) and <b>Other</b>
/// (whatever no configured category claims) buckets, plus one per <see cref="AssetBrowserCategory"/> the
/// user configured (Settings ▸ Editor ▸ Asset Browser). It carries the live <see cref="Count"/> the rail
/// shows and the <see cref="Accepts"/> predicate the browser filters with — the same matching logic drives
/// both, so a bucket's count and its contents can't drift apart.
/// </summary>
public sealed partial class AssetCategoryViewModel : ObservableObject
{
    private enum Bucket { All, Category, Other }

    private readonly Bucket _bucket;
    private readonly AssetBrowserCategory? _category;
    private readonly IReadOnlyList<AssetBrowserCategory> _configured;

    private AssetCategoryViewModel(
        Bucket bucket, string label, AssetBrowserCategory? category,
        IReadOnlyList<AssetBrowserCategory> configured)
    {
        _bucket = bucket;
        Label = label;
        _category = category;
        _configured = configured;

        Icon = category is { } configuredCategory
            ? CategoryVisuals.IconFor(configuredCategory.Icon)
            : bucket == Bucket.All ? Icon.Boxes : Icon.Folder;
        IconBrush = CategoryVisuals.BrushFor(category?.Color ?? CategoryColor.Default);
    }

    /// <summary>The rail label (the bucket name or the configured category's name).</summary>
    public string Label { get; }

    /// <summary>The rail icon: the configured category's, or a bucket default for All/Other.</summary>
    public Icon Icon { get; }

    /// <summary>The rail icon's accent tint (matching the tiles the bucket contains).</summary>
    public IBrush IconBrush { get; }

    [ObservableProperty]
    public partial int Count { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The catch-all bucket: accepts every asset.</summary>
    public static AssetCategoryViewModel All() => new(Bucket.All, "All", null, []);

    /// <summary>A configured category: accepts the assets its patterns claim.</summary>
    public static AssetCategoryViewModel For(AssetBrowserCategory category) =>
        new(Bucket.Category, category.Name, category, []);

    /// <summary>The leftovers bucket: accepts assets no configured category claims.</summary>
    public static AssetCategoryViewModel Other(IReadOnlyList<AssetBrowserCategory> configured) =>
        new(Bucket.Other, "Other", null, configured);

    /// <summary>Whether this bucket contains the asset.</summary>
    public bool Accepts(AssetEntry asset) => _bucket switch
    {
        Bucket.All => true,
        Bucket.Category => _category is { } category && AssetCategoryMatcher.Matches(category, asset),
        Bucket.Other => !_configured.Any(category => AssetCategoryMatcher.Matches(category, asset)),
        _ => false,
    };
}
