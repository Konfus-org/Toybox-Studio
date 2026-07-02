using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Logging;
using Toybox.Studio.Project;
using Toybox.Studio.Project.Assets;
using Toybox.Studio.Settings;
using Toybox.Studio.Worlds;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// The Asset Browser panel: a dockable, searchable, filterable view of the project's assets. It listens to the
/// <see cref="AssetCatalog"/> for content and to <see cref="SettingsManager"/> for the user-defined category
/// list (Settings ▸ Editor ▸ Asset Browser), and projects both into a collection rail (with live per-category
/// counts) and a grid/list of <see cref="AssetTileViewModel"/> tiles. Opening an asset is type-routed through
/// the <see cref="AssetPipeline"/> (which delegates to the shared <see cref="AssetOpener"/>): double-tapping
/// (or the right-click "Open") reuses the open window; the right-click "Open in new window" forces a fresh
/// one. Live 3D thumbnails, the hover HUD, and the
/// import/auto-meta pipeline land in later phases.
///
/// Engine/bridge built-ins and bookkeeping (plain .meta sidecars, dotfile configs) are excluded.
/// </summary>
public sealed partial class AssetBrowserViewModel : ObservableObject, IDisposable
{
    private readonly AssetCatalog _catalog;
    private readonly AssetFactory _factory;
    private readonly SettingsManager _settings;
    private readonly AssetSelection _assetSelection;
    private readonly ProjectManager _projects;
    private readonly HoverPreviewViewModel _hoverPreview;
    private readonly IDisposable _catalogSubscription;
    private readonly IDisposable _settingsSubscription;

    // Persistent tiles, keyed by path (NOT id: the catalog can report many assets with id 0).
    private readonly Dictionary<string, AssetTileViewModel> _tilesByPath = new(StringComparer.OrdinalIgnoreCase);

    // The merged category list backing the rail. Rebuilt — minting fresh AssetCategory instances — only in
    // RebuildCollections, and reused by every Refresh. The rail entries and the per-asset Match results must be
    // the SAME instances, because AssetCollectionViewModel.Accepts compares them by reference; re-deriving the
    // list per Refresh (MergedWithDefaults mints new instances each call) made every category silently empty.
    private IReadOnlyList<AssetCategory> _categories = [];

    private bool _hasSourceAssets;

    public AssetBrowserViewModel(
        AssetCatalog catalog, AssetFactory factory, SettingsManager settings,
        AssetSelection assetSelection, ProjectManager projects, AssetViewerLauncher viewerLauncher,
        Session session, Engine engine, IEngineSyncScheduler scheduler,
        WorldSelection selection, EngineWatcher watcher, Logger logger)
    {
        _catalog = catalog;
        _factory = factory;
        _settings = settings;
        _assetSelection = assetSelection;
        _projects = projects;
        _hoverPreview = new HoverPreviewViewModel(
            viewerLauncher, session, engine, scheduler, catalog, selection, watcher, logger);

        // The settings drive the rail; fire once now to build it (and the first tile pass). The catalog
        // listener only refreshes counts + tiles (the rail is already built), so it skips the immediate fire.
        _settingsSubscription = _settings.Listen(() => Dispatch.To(DispatchContext.UI, RebuildCollections));
        _catalogSubscription = _catalog.Listen(() => Dispatch.To(DispatchContext.UI, Refresh), fireImmediately: false);

        // Keep the highlighted tile in step with the shared asset selection (e.g. after a create/duplicate
        // selects the new asset from outside the browser).
        _assetSelection.Changed += () => Dispatch.To(DispatchContext.UI, SyncSelectionFromShared);
    }

    /// <summary>The rail entries: "All", one per configured category, then "Other".</summary>
    public ObservableCollection<AssetCollectionViewModel> Collections { get; } = [];

    /// <summary>The filtered, searched, alphabetised tiles the grid/list binds to.</summary>
    public ObservableCollection<AssetTileViewModel> Tiles { get; } = [];

    /// <summary>The single live hover preview (the card binds its surface + currently-hovered tile).</summary>
    public HoverPreviewViewModel HoverPreview => _hoverPreview;

    [ObservableProperty]
    public partial AssetCollectionViewModel? SelectedCollection { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsListView { get; set; }

    [ObservableProperty]
    public partial AssetTileViewModel? SelectedTile { get; set; }

    public bool HasResults => Tiles.Count > 0;

    /// <summary>Empty-state ghost: the project genuinely has no (non-builtin) assets.</summary>
    public bool ShowEmpty => !HasResults && !_hasSourceAssets;

    /// <summary>Empty-state ghost: there are assets, but none match the current filter/search.</summary>
    public bool ShowNoMatches => !HasResults && _hasSourceAssets;

    /// <summary>How many project assets are missing a <c>.meta</c> sidecar — drives the "generate metadata" banner.</summary>
    public int MissingMetaCount { get; private set; }

    /// <summary>Whether to show the "N assets missing metadata" banner.</summary>
    public bool ShowMissingMetaBanner => MissingMetaCount > 0;

    /// <summary>The banner caption, e.g. "3 assets are missing metadata".</summary>
    public string MissingMetaText =>
        MissingMetaCount == 1 ? "1 asset is missing metadata" : $"{MissingMetaCount} assets are missing metadata";

    public void Dispose()
    {
        _catalogSubscription.Dispose();
        _settingsSubscription.Dispose();
        _hoverPreview.Dispose();
    }

    /// <summary>Dismisses the hover-preview card when the pointer leaves the asset grid.</summary>
    [RelayCommand]
    private void ClearHover() => _hoverPreview.ClearCurrent();

    [RelayCommand]
    private void SelectTile(AssetTileViewModel tile)
    {
        if (ReferenceEquals(SelectedTile, tile))
            return;

        if (SelectedTile is { } previous)
            previous.IsSelected = false;

        tile.IsSelected = true;
        SelectedTile = tile;

        // Drive the shared asset selection so the Inspector shows this asset's properties (the inspector clears
        // the entity selection in turn, so the single inspector shows one thing at a time).
        _assetSelection.Select(new AssetHandle(tile.Id, tile.Asset.Name, tile.Asset.Type, tile.Asset.Path));
    }

    // Mirrors an externally-driven asset selection (e.g. after Create/Duplicate) onto the highlighted tile.
    private void SyncSelectionFromShared()
    {
        var path = _assetSelection.Current.Path;
        if (string.IsNullOrEmpty(path))
            return;

        var tile = Tiles.FirstOrDefault(candidate =>
            string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
        if (tile is null || ReferenceEquals(SelectedTile, tile))
            return;

        if (SelectedTile is { } previous)
            previous.IsSelected = false;
        tile.IsSelected = true;
        SelectedTile = tile;
    }

    [RelayCommand]
    private Task DeleteSelectedAsync() =>
        SelectedTile is { } tile ? _factory.For(tile.Asset).DeleteAsync() : Task.CompletedTask;

    [RelayCommand]
    private Task RenameSelectedAsync() =>
        SelectedTile is { } tile ? _factory.For(tile.Asset).RenameAsync() : Task.CompletedTask;

    [RelayCommand]
    private Task DuplicateSelectedAsync() =>
        SelectedTile is { } tile ? _factory.For(tile.Asset).DuplicateAsync() : Task.CompletedTask;

    [RelayCommand]
    private Task CopySelectedAsync() =>
        SelectedTile is { } tile ? _factory.For(tile.Asset).CopyAsync() : Task.CompletedTask;

    [RelayCommand]
    private Task PasteAsync() =>
        SelectedTile is { } tile ? _factory.PasteAsync(_factory.For(tile.Asset)) : _factory.PasteAsync();

    /// <summary>Opens the selected asset (fired on double-tap), reusing the open window — the default route.</summary>
    [RelayCommand]
    private Task ActivateSelectedAsync()
    {
        return SelectedTile is { } tile ? _factory.For(tile.Asset).OpenAsync() : Task.CompletedTask;
    }

    // Opens a tile's asset, reusing the open window (the right-click "Open"); passed to each tile so the
    // context menu can bind to the tile's own DataContext.
    private void OpenTile(AssetTileViewModel tile) => _factory.For(tile.Asset).OpenAsync().FireAndForget();

    // Opens a tile's asset in a fresh window (the right-click "Open in new window").
    private void OpenTileInNewWindow(AssetTileViewModel tile) =>
        _factory.For(tile.Asset).OpenAsync(newWindow: true).FireAndForget();

    // Fetches a tile's hover-HUD stats (engine-unit bounds, tri/material counts); passed to each tile so it
    // can lazily load on first hover.
    private Task<AssetPreviewStats?> FetchStatsAsync(ulong id) => _catalog.PreviewStatsAsync(id);

    [RelayCommand]
    private void ShowGrid() => IsListView = false;

    [RelayCommand]
    private void ShowList() => IsListView = true;

    [RelayCommand]
    private Task ReloadAsync() => _catalog.RefreshAsync();

    /// <summary>Generates the missing <c>.meta</c> sidecars (the banner's action); the catalog refresh that
    /// follows rebuilds the grid and clears the banner.</summary>
    [RelayCommand]
    private Task GenerateMetasAsync() => _catalog.GenerateMissingMetasAsync();

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnSelectedCollectionChanged(AssetCollectionViewModel? value) => Refresh();

    // Rebuilds the rail from the configured categories, preserving the current selection by label, then refreshes
    // the counts and tiles. Runs whenever the category settings change (and once at startup). Caches the merged
    // list in _categories so Refresh matches assets against the very instances the rail was built from.
    private void RebuildCollections()
    {
        var previousLabel = SelectedCollection?.Label;

        _categories = AssetCategory.MergedWithDefaults(_settings.Settings.AssetBrowser?.Categories ?? []);

        Collections.Clear();
        Collections.Add(AssetCollectionViewModel.All());
        foreach (var category in _categories)
            Collections.Add(AssetCollectionViewModel.For(category));
        Collections.Add(AssetCollectionViewModel.Other());

        SelectedCollection = Collections.FirstOrDefault(collection => collection.Label == previousLabel)
                             ?? Collections[0];

        Refresh();
    }

    private void Refresh()
    {
        // RebuildCollections always sets a selection before the first Refresh; guard defensively anyway.
        if (SelectedCollection is null)
            return;

        var categories = _categories;
        var projectRoot = _projects.CurrentProject?.RootDirectory ?? "";
        var source = _catalog.Assets
            .Where(asset => !asset.IsBuiltin
                            && !AssetCategoryMatcher.IsHidden(asset)
                            && AssetCategoryMatcher.IsProjectContent(asset, projectRoot))
            .ToList();
        _hasSourceAssets = source.Count > 0;
        MissingMetaCount = source.Count(asset => !asset.HasMeta);

        // Resolve each asset to its category once (by path), then reuse for both counts and filtering.
        var assigned = new Dictionary<string, AssetCategory?>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in source)
            assigned[asset.Path] = AssetCategoryMatcher.Match(categories, asset);

        foreach (var collection in Collections)
            collection.Count = source.Count(asset => collection.Accepts(assigned[asset.Path]));

        var filtered = source
            .Where(asset => SelectedCollection.Accepts(assigned[asset.Path]))
            .Where(MatchesSearch)
            .OrderBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Reuse a persistent tile per path; rebuild it only if the underlying record was replaced.
        var desired = new List<AssetTileViewModel>(filtered.Count);
        foreach (var asset in filtered)
        {
            if (!_tilesByPath.TryGetValue(asset.Path, out var tile) || !ReferenceEquals(tile.Asset, asset))
            {
                tile = new AssetTileViewModel(asset, OpenTile, OpenTileInNewWindow, FetchStatsAsync, _hoverPreview);
                _tilesByPath[asset.Path] = tile;
            }

            tile.Category = assigned[asset.Path];
            desired.Add(tile);
        }

        // Evict cached tiles whose asset left the catalog entirely (not merely filtered out of the view).
        var present = source.Select(asset => asset.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _tilesByPath.Keys.Where(path => !present.Contains(path)).ToList())
            _tilesByPath.Remove(path);

        Tiles.Reconcile(desired);

        // Drop a selection whose tile is no longer shown.
        if (SelectedTile is { } selected && !desired.Contains(selected))
        {
            selected.IsSelected = false;
            SelectedTile = null;
        }

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowNoMatches));
        OnPropertyChanged(nameof(MissingMetaCount));
        OnPropertyChanged(nameof(ShowMissingMetaBanner));
        OnPropertyChanged(nameof(MissingMetaText));
    }

    private bool MatchesSearch(AssetMeta asset)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var query = SearchText.Trim();
        return asset.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || asset.Type.Contains(query, StringComparison.OrdinalIgnoreCase)
               || asset.Path.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
