using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Settings;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// One entry in the browser's collection rail. Three flavours: "All" (matches everything), one per user-defined
/// <see cref="AssetCategory"/>, and the built-in "Other" (assets no category claimed). The live
/// <see cref="Count"/> is refreshed by the <see cref="AssetBrowserViewModel"/> whenever the catalog or the
/// category settings change.
/// </summary>
public sealed partial class AssetCollectionViewModel : ObservableObject
{
    private AssetCollectionViewModel(string label, Icon icon, AssetCategory? category, bool isAll, bool isOther)
    {
        Label = label;
        Icon = icon;
        Category = category;
        IsAll = isAll;
        IsOther = isOther;
    }

    public string Label { get; }

    public Icon Icon { get; }

    /// <summary>The category this entry filters to (null for "All" and "Other").</summary>
    public AssetCategory? Category { get; }

    public bool IsAll { get; }

    public bool IsOther { get; }

    /// <summary>How many (non-builtin, non-hidden) assets fall into this entry.</summary>
    [ObservableProperty]
    public partial int Count { get; set; }

    public static AssetCollectionViewModel All() => new("All", Icon.LayoutGrid, null, isAll: true, isOther: false);

    public static AssetCollectionViewModel Other() => new("Other", Icon.File, null, isAll: false, isOther: true);

    public static AssetCollectionViewModel For(AssetCategory category) =>
        new(string.IsNullOrWhiteSpace(category.Name) ? "Unnamed" : category.Name,
            category.Icon == Icon.None ? Icon.File : category.Icon, category, isAll: false, isOther: false);

    /// <summary>Whether an asset (already resolved to its <paramref name="assigned"/> category, or null) belongs here.</summary>
    public bool Accepts(AssetCategory? assigned) =>
        IsAll || (IsOther ? assigned is null : ReferenceEquals(assigned, Category));
}
