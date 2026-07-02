using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.Project;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// One asset shown in the browser grid (or list). A persistent view-model — the browser keeps the same instance
/// for an asset across catalog refreshes (keyed by <see cref="Path"/>, since the catalog can hand out a shared
/// id of 0 to assets without a stable id) so selection survives a reconcile. Its glyph follows the
/// <see cref="Category"/> the browser assigned it. The <see cref="OpenCommand"/> / <see cref="OpenInNewWindowCommand"/>
/// back the right-click menu (delegating to browser-supplied callbacks so the menu can bind to the tile's own
/// DataContext). For models, the hover HUD's engine-unit scale is fetched lazily on first hover via
/// <see cref="LoadStatsCommand"/>.
/// </summary>
public sealed partial class AssetTileViewModel : ObservableObject
{
    private readonly Action<AssetTileViewModel> _open;
    private readonly Action<AssetTileViewModel> _openInNewWindow;
    private readonly Func<ulong, Task<AssetPreviewStats?>> _fetchStats;
    private readonly HoverPreviewViewModel _hoverPreview;
    private bool _statsRequested;

    public AssetTileViewModel(
        AssetMeta asset,
        Action<AssetTileViewModel> open,
        Action<AssetTileViewModel> openInNewWindow,
        Func<ulong, Task<AssetPreviewStats?>> fetchStats,
        HoverPreviewViewModel hoverPreview)
    {
        Asset = asset;
        _open = open;
        _openInNewWindow = openInNewWindow;
        _fetchStats = fetchStats;
        _hoverPreview = hoverPreview;
    }

    public AssetMeta Asset { get; }

    public ulong Id => Asset.Id;

    /// <summary>The project-relative path — the tile's stable identity and tooltip footer.</summary>
    public string Path => Asset.Path;

    public string Name => Asset.Name;

    /// <summary>The name shown in the grid/list: a C++ script drops its source extension ("Player.h" → "Player");
    /// every other asset shows its file name as-is. Distinct from <see cref="Name"/>, which the category matcher
    /// still reads with the extension intact.</summary>
    public string DisplayName
    {
        get
        {
            if (!Asset.IsScript)
                return Asset.Name;
            var dot = Asset.Name.LastIndexOf('.');
            return dot > 0 ? Asset.Name[..dot] : Asset.Name;
        }
    }

    /// <summary>The short type pill text — the upper-cased extension (e.g. "FBX"), or "C++" for a C++ script.</summary>
    public string TypeLabel => AssetCategoryMatcher.TypeLabel(Asset);

    /// <summary>The category the browser assigned this asset (null = the "Other" bucket); drives the glyph.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon))]
    [NotifyPropertyChangedFor(nameof(IconColor))]
    public partial AssetCategory? Category { get; set; }

    public Icon Icon => Category is { Icon: var icon } && icon != Icon.None ? icon : Icon.File;

    public Avalonia.Media.Color? IconColor =>
        (Category?.Color ?? PaletteColor.None).ToColor() ?? Toybox.Studio.Utils.Colors.Grey;

    /// <summary>Whether this is a 3D model — the only kind with an engine-unit scale in the hover card.</summary>
    public bool IsModelTile => AssetCategoryMatcher.IsModel(Asset);

    /// <summary>Whether this asset gets a live 3D hover preview (a model, material, or texture).</summary>
    public bool IsPreviewableTile => AssetCategoryMatcher.IsPreviewable(Asset);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The model's real-world stats for the hover HUD, fetched lazily on first hover (null until then,
    /// or for non-models).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScale))]
    [NotifyPropertyChangedFor(nameof(ScaleText))]
    [NotifyPropertyChangedFor(nameof(DetailText))]
    public partial AssetPreviewStats? Stats { get; set; }

    /// <summary>True once a model's measurable bounds are known — gates the HUD's scale row.</summary>
    public bool HasScale => Stats is { Width: > 0 };

    /// <summary>The engine-unit dimensions, e.g. "1.20 × 1.05 × 1.20 m" (empty until <see cref="Stats"/> loads).</summary>
    public string ScaleText =>
        Stats is { Width: > 0 } s
            ? $"{Format(s.Width)} × {Format(s.Height)} × {Format(s.Depth)} m"
            : "";

    /// <summary>The secondary HUD line: triangle and material counts (empty until <see cref="Stats"/> loads).</summary>
    public string DetailText =>
        Stats is { } s
            ? $"{s.Triangles.ToString("N0", CultureInfo.CurrentCulture)} tris · "
              + $"{s.Materials} material{(s.Materials == 1 ? "" : "s")}"
            : "";

    /// <summary>Opens the asset, reusing the existing window when applicable (the default open).</summary>
    [RelayCommand]
    private void Open() => _open(this);

    /// <summary>Opens the asset in a new window.</summary>
    [RelayCommand]
    private void OpenInNewWindow() => _openInNewWindow(this);

    /// <summary>Handles the pointer entering this tile: a previewable asset (model/material/texture) wakes the
    /// single live hover preview; a model additionally loads its engine-unit scale; anything else dismisses
    /// the card.</summary>
    [RelayCommand]
    private void Hover()
    {
        if (IsPreviewableTile)
        {
            _hoverPreview.Show(this);
            if (IsModelTile)
                EnsureStats();
        }
        else
        {
            _hoverPreview.ClearCurrent();
        }
    }

    // Fetches the model's stats once (engine-unit bounds, tri/material counts) for the hover card's scale row.
    private void EnsureStats()
    {
        if (_statsRequested || Id == 0)
            return;

        _statsRequested = true;
        LoadStatsAsync().FireAndForget();
        return;

        async Task LoadStatsAsync()
        {
            var stats = await _fetchStats(Id).ContinueOnAnyContext();
            if (stats is not null)
                Dispatch.To(DispatchContext.UI, () => Stats = stats);
        }
    }

    // A compact dimension: up to two decimals, no trailing zeros (e.g. "1.2", "0.45", "12").
    private static string Format(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);
}
