using System.IO;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.AssetViewer;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.Settings;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// One asset tile: a catalog row shaped for the grid. Its icon and icon colour come from the category it
/// matched (mapped from that category's editor-defined icon/colour); an asset that matched no category
/// falls back to a kind icon and no tint. It flags "missing metadata" only for real engine assets — the
/// support files categories like CMake/Documentation collect (cmake, clang, readme…) aren't engine assets
/// and never carry a <c>.meta</c>, so they're never flagged. Opening routes through the shared
/// <see cref="AssetOpener"/> — double-tap / "Open" reuses the open editor, "Open in New Window" forces a
/// fresh one. It only ever hands the opener its <see cref="AssetEntry"/>; it knows nothing of JSON or RPC.
/// </summary>
public sealed partial class AssetTileViewModel : ObservableObject, IAssetMenuTarget
{
    private readonly AssetOpener _opener;
    private readonly AssetBrowserCategory? _category;

    public AssetTileViewModel(
        AssetEntry entry, AssetBrowserCategory? category, AssetOpener opener, AssetBrowserViewModel browser)
    {
        Entry = entry;
        _category = category;
        _opener = opener;
        Browser = browser;
    }

    public AssetEntry Entry { get; }

    /// <summary>The owning panel — the shared menu's create rows route through its commands.</summary>
    public AssetBrowserViewModel Browser { get; }

    /// <summary>This tile is itself the menu target, so right-clicking it shows its per-tile verbs.</summary>
    public AssetTileViewModel? Tile => this;

    public string Name => Entry.Name;

    /// <summary>The label shown under the tile: the asset name, with a script's source extension stripped
    /// (a script row's name carries its <c>.h</c>/<c>.cpp</c>; everything else shows as-is).</summary>
    public string DisplayName => Entry.IsScript ? Path.GetFileNameWithoutExtension(Entry.Name) : Name;

    /// <summary>The extension token as a short badge ("MAT", "PNG", …).</summary>
    public string TypeLabel => Entry.Type.ToUpperInvariant();

    /// <summary>Whether hovering the tile shows a live 3D turntable (a model, material, or texture).</summary>
    public bool IsPreviewable => AssetClassifier.IsPreviewable(Entry);

    /// <summary>Whether this tile is the grid's current selection — drives the accent selection frame. Kept in
    /// sync by the browser from the grid's <c>SelectedItem</c>.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>
    /// A real engine asset with no <c>.meta</c> sidecar — flagged so the tile can badge it. Only assets a
    /// payload class actually claims (<see cref="AssetKinds.ForExtension"/>) can carry a <c>.meta</c>; the
    /// support files categories collect (cmake, clang, readme…) never need one, so they aren't flagged.
    /// </summary>
    public bool MissingMeta =>
        !Entry.HasMeta && !Entry.IsBuiltin && !Entry.IsScript && AssetKinds.ForExtension(Entry.Type) is not null;

    /// <summary>The kind icon: from the matched category, else a fallback from the asset's own kind.</summary>
    public Icon Icon => _category is { } category ? CategoryVisuals.IconFor(category.Icon) : FallbackIcon(Entry);

    /// <summary>The icon tint: the matched category's accent colour, else the neutral default.</summary>
    public IBrush IconBrush =>
        CategoryVisuals.BrushFor(_category?.Color ?? CategoryColor.Default);

    [RelayCommand]
    private void Open() => _opener.Open(Entry);

    [RelayCommand]
    private void OpenInNewWindow() => _opener.OpenInNewWindow(Entry);

    /// <summary>Pointer entered the tile — asks the browser to show (or hide, for a non-previewable asset) the
    /// live hover turntable for this row.</summary>
    [RelayCommand]
    private void Hover() => Browser.HoverTile(this);

    // A sensible icon for an asset that matched no configured category, from the asset class its extension
    // resolves to.
    private static Icon FallbackIcon(AssetEntry entry)
    {
        if (AssetClassifier.IsMaterial(entry.Type))
            return Icon.Palette;
        if (AssetClassifier.IsTexture(entry.Type))
            return Icon.Image;
        if (AssetClassifier.IsModel(entry.Type))
            return Icon.Box;
        if (AssetKinds.Is<Shader>(entry.Type))
            return Icon.Sparkles;
        if (AssetKinds.Is<AudioClip>(entry.Type))
            return Icon.Music;
        if (AssetKinds.Is<InputMap>(entry.Type))
            return Icon.Gamepad2;
        return entry.IsScript ? Icon.Code : Icon.File;
    }
}
