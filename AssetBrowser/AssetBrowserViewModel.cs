using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Toybox.Studio.AssetViewer;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi.Types.Assets;
using Toybox.Studio.Events;
using Toybox.Studio.Logging;
using Toybox.Studio.Projects;
using Toybox.Studio.Settings;
using Toybox.Studio.Utils.Composition;
using Toybox.Studio.Utils.Extensions;
using Toybox.Studio.Utils.Search;
using Toybox.Studio.Utils;

namespace Toybox.Studio.AssetBrowser;

/// <summary>
/// The Asset Browser panel: a dockable, searchable, category-filtered view of the project's assets. It
/// reads its content from the typed <see cref="AssetCatalog"/> and its category layout from the editor
/// settings (Settings ▸ Editor ▸ Asset Browser), projecting both into a rail of
/// <see cref="AssetCategoryViewModel"/> buckets (with live counts) and a grid of
/// <see cref="AssetTileViewModel"/> tiles. Double-tapping a tile opens it through the shared
/// <see cref="AssetOpener"/> (previewable assets land in the Asset Viewer editor); the empty-space menu
/// authors new assets through the typed <see cref="Asset"/> lifecycle. It touches no JSON or RPC — only
/// the catalog rows and the typed asset API.
/// </summary>
public sealed partial class AssetBrowserViewModel : ObservableEventSubscriber,
    IEventHandler<AssetCatalogChanged>, IEventHandler<EditorSettingsChanged>, IAssetMenuTarget
{
    private readonly AssetCatalog _catalog;
    private readonly SettingsManager _settings;
    private readonly AssetOpener _opener;
    private readonly AssetOperations _operations;
    private readonly Project _project;
    private readonly Popups _popups;
    private readonly Logger _log;

    // Tiles kept by path (the stable key — the catalog can report many assets with id 0), so a refresh
    // reuses the same tile instance for an unchanged asset (preserving selection) and only rebuilds one
    // whose underlying row changed.
    private readonly Dictionary<string, AssetTileViewModel> _tilesByPath = new(StringComparer.OrdinalIgnoreCase);

    // The category definitions the rail was last built from; the buckets match assets against these.
    private IReadOnlyList<AssetBrowserCategory> _categoryDefs = [];

    public AssetBrowserViewModel(
        AssetCatalog catalog, SettingsManager settings, AssetOpener opener, AssetOperations operations,
        Project project, EventDispatcher events, Popups popups, Logger log, ViewModelFactory viewModels)
        : base(events)
    {
        _catalog = catalog;
        _settings = settings;
        _opener = opener;
        _operations = operations;
        _project = project;
        _popups = popups;
        _log = log;

        // The live hover turntable — its own asset-preview stream + surface, torn down between hovers.
        HoverPreview = viewModels.Create<HoverPreviewViewModel>();

        // Text matching runs off the UI thread through the search controller; the snapshot (category
        // buckets, counts, tile eviction) and applying results (tile build + reconcile) stay on the UI
        // thread. Created before the first category selection, which schedules the initial pass.
        Search = new SearchController<AssetEntry>(
            new SubstringSearchStrategy<AssetEntry>(entry => entry.Name, entry => entry.Type, entry => entry.Path),
            Snapshot, ApplyResults);

        // Selecting a bucket schedules the first tile pass; the catalog is already populated by the
        // time a panel opens.
        RebuildCategories();
    }

    /// <summary>Drives the search field: its <c>Query</c> is the search text, <c>IsBusy</c> the spinner.</summary>
    public SearchController<AssetEntry> Search { get; }

    /// <summary>The rail buckets: "All", one per configured category, then "Other".</summary>
    public ObservableCollection<AssetCategoryViewModel> Categories { get; } = [];

    /// <summary>The filtered, searched, alphabetised tiles the grid binds to.</summary>
    public ObservableCollection<AssetTileViewModel> Tiles { get; } = [];

    [ObservableProperty]
    public partial AssetCategoryViewModel? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial AssetTileViewModel? SelectedTile { get; set; }

    /// <summary>The live hover preview: a floating 3D turntable of the asset under the pointer. Shown by the
    /// grid's hover card whenever <see cref="HoverPreviewViewModel.Current"/> is non-null.</summary>
    public HoverPreviewViewModel HoverPreview { get; }

    /// <summary>Grid (cards) vs list (rows) content mode, toggled by the toolbar's segmented control.</summary>
    [ObservableProperty]
    public partial bool IsListView { get; set; }

    /// <summary>The empty grid space routes its menu here, so the right-clicked tile is "none".</summary>
    AssetBrowserViewModel IAssetMenuTarget.Browser => this;
    AssetTileViewModel? IAssetMenuTarget.Tile => null;

    public bool HasResults => Tiles.Count > 0;

    /// <summary>Empty-state ghost when nothing matches the current filter/search.</summary>
    public bool IsEmpty => Tiles.Count == 0;

    /// <summary>The catalog swapped in a new listing (refresh, create, disconnect): rebuild the grid.</summary>
    public void Handle(in AssetCatalogChanged evt) => Dispatch.To(DispatchContext.UI, Search.Refresh);

    /// <summary>The editor settings changed: the category layout may have — rebuild the rail and grid.</summary>
    public void Handle(in EditorSettingsChanged evt) => Dispatch.To(DispatchContext.UI, RebuildCategories);

    [RelayCommand]
    private Task CreateMaterialAsync() => CreateAsync(new Material("Material", "Assets/Materials"));

    [RelayCommand]
    private Task CreateInputMapAsync() => CreateAsync(new InputMap("Input Map", "Assets"));

    [RelayCommand]
    private Task ReloadAsync() => _catalog.RefreshAsync();

    [RelayCommand]
    private void ShowGrid() => IsListView = false;

    [RelayCommand]
    private void ShowList() => IsListView = true;

    /// <summary>The pointer entered <paramref name="tile"/>: show its live turntable, or clear the card when the
    /// asset isn't previewable (a script, a support file). Called from the tile's hover command.</summary>
    public void HoverTile(AssetTileViewModel tile)
    {
        if (tile.IsPreviewable)
            HoverPreview.Show(tile.Entry);
        else
            HoverPreview.Clear();
    }

    // The pointer left the grid: tear the hover turntable down (and stop its engine view).
    [RelayCommand]
    private void ClearHover() => HoverPreview.Clear();

    // The selected-tile CRUD verbs the keyboard shortcuts drive (the context menu drives the same operations
    // on the right-clicked tile). Each is a no-op with nothing selected; Paste needs only a clipboard asset.
    [RelayCommand]
    private Task RenameSelected() =>
        SelectedTile is { } tile ? _operations.RenameAsync(tile.Entry) : Task.CompletedTask;

    [RelayCommand]
    private Task DuplicateSelected() =>
        SelectedTile is { } tile ? _operations.DuplicateAsync(tile.Entry) : Task.CompletedTask;

    [RelayCommand]
    private Task CopySelected() =>
        SelectedTile is { } tile ? _operations.CopyAsync(tile.Entry) : Task.CompletedTask;

    [RelayCommand]
    private Task DeleteSelected() =>
        SelectedTile is { } tile ? _operations.DeleteAsync(tile.Entry) : Task.CompletedTask;

    [RelayCommand]
    private Task Paste() => _operations.PasteAsync();

    public override void Dispose()
    {
        HoverPreview.Dispose();
        base.Dispose();
    }

    partial void OnSelectedCategoryChanged(AssetCategoryViewModel? value) => Search.Refresh();

    // Mirror the grid's selection onto the tiles so the selected card reads the accent frame (the grid's
    // SelectedItem drives this; only one tile is ever selected).
    partial void OnSelectedTileChanged(AssetTileViewModel? oldValue, AssetTileViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;
        if (newValue is not null)
            newValue.IsSelected = true;
    }

    // Authors a fresh asset through the typed lifecycle and, once it lands, opens it. SaveAsync refreshes
    // the catalog (rebuilding the grid via the change event), so the new row is resolvable here.
    private async Task CreateAsync(Asset asset)
    {
        var saved = await asset.SaveAsync().ContinueOnSameContext();
        if (!saved)
        {
            _log.Error($"Couldn't create the asset: {saved.Error}");
            await _popups.ErrorAsync("Create Asset", $"Couldn't create the asset: {saved.Error}")
                .ContinueOnSameContext();
            return;
        }

        if (_catalog.Find(asset.Id) is { } entry)
            _opener.Open(entry);
    }

    // Rebuilds the rail from the configured categories, preserving the current selection by label; the
    // selection change runs the tile refresh. Caches the definitions so the tile pass matches assets
    // against the very list the rail was built from.
    private void RebuildCategories()
    {
        var previous = SelectedCategory?.Label;
        _categoryDefs = _settings.Editor.EditorAssetSettings.Categories;
        _tilesByPath.Clear(); // categories changed → tiles rebuild with the new icons/colours

        var rail = new List<AssetCategoryViewModel> { AssetCategoryViewModel.All() };
        rail.AddRange(_categoryDefs.Select(AssetCategoryViewModel.For));
        rail.Add(AssetCategoryViewModel.Other(_categoryDefs));

        Categories.Clear();
        foreach (var bucket in rail)
            Categories.Add(bucket);

        // The rail is rebuilt from fresh instances, so assigning the selection always changes it — which
        // runs the tile/count refresh through OnSelectedCategoryChanged.
        SelectedCategory = Categories.FirstOrDefault(bucket => bucket.Label == previous) ?? Categories[0];
    }

    // (UI thread) The candidate set the search runs over: the selected category's project content,
    // alphabetised. Also refreshes the bucket counts and evicts tiles whose asset left the catalog —
    // both cheap and dependent only on the source, so they belong here rather than on the query result.
    private IReadOnlyList<AssetEntry> Snapshot()
    {
        if (SelectedCategory is null)
            return [];

        // Show only the open project's own content: drop engine resources and the editor's asset-viewer
        // built-ins (other registered roots), the in-memory preview built-ins, and config sidecars.
        var source = _catalog.Entries
            .Where(entry => !entry.IsBuiltin
                && AssetCategoryMatcher.IsProjectContent(entry, _project.Path)
                && !IsHidden(entry))
            .ToList();

        foreach (var bucket in Categories)
            bucket.Count = source.Count(bucket.Accepts);

        // Evict cached tiles whose asset left the catalog (not merely filtered out of the view).
        var present = source.Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _tilesByPath.Keys.Where(path => !present.Contains(path)).ToList())
            _tilesByPath.Remove(path);

        return source
            .Where(SelectedCategory.Accepts)
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // (UI thread) Projects the matched entries into cached tiles and reconciles the grid in place.
    private void ApplyResults(IReadOnlyList<AssetEntry> entries)
    {
        var desired = new List<AssetTileViewModel>(entries.Count);
        foreach (var entry in entries)
        {
            if (!_tilesByPath.TryGetValue(entry.Path, out var tile) || !tile.Entry.Equals(entry))
            {
                var category = _categoryDefs.FirstOrDefault(candidate => AssetCategoryMatcher.Matches(candidate, entry));
                tile = new AssetTileViewModel(entry, category, _opener, this);
                _tilesByPath[entry.Path] = tile;
            }
            desired.Add(tile);
        }

        Tiles.Reconcile(desired);

        if (SelectedTile is { } selected && !desired.Contains(selected))
            SelectedTile = null;

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(IsEmpty));
    }

    // Editor/engine settings files (AppSettings.json, …) are configuration, not project content — kept
    // out of the browser.
    private static bool IsHidden(AssetEntry entry) =>
        entry.Path.EndsWith("Settings.json", StringComparison.OrdinalIgnoreCase);
}
