using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Project;
using Toybox.Studio.Worlds;
using Toybox.Studio.Utils;
using Toybox.Studio.Ecs;
using Toybox.Studio.WorldTree;

namespace Toybox.Studio.EntityInspector;

/// <summary>
/// The Inspector dockable's view-model: it shows the selected ENTITY (the world view's component grids) or the
/// selected ASSET (the buffered asset property grid), whichever is active. The two selections are mutually
/// exclusive — selecting an asset clears the entity selection and vice-versa — so the single panel always shows
/// one thing at a time. <see cref="World"/> and <see cref="Asset"/> are the two content view-models the view
/// switches between on <see cref="ShowAsset"/>.
/// </summary>
public sealed partial class EntityInspectorViewModel : ObservableObject
{
    private readonly WorldSelection _worldSelection;
    private readonly AssetSelection _assetSelection;

    public EntityInspectorViewModel(
        WorldViewModel world,
        AssetInspectorViewModel asset,
        WorldSelection worldSelection,
        AssetSelection assetSelection)
    {
        World = world;
        Asset = asset;
        _worldSelection = worldSelection;
        _assetSelection = assetSelection;

        _worldSelection.SelectionChanged +=
            () => Dispatch.To(DispatchContext.UI, OnWorldSelectionChanged);
        _assetSelection.Changed +=
            () => Dispatch.To(DispatchContext.UI, OnAssetSelectionChanged);
    }

    /// <summary>The entity inspector content (world tree + viewport share this same instance).</summary>
    public WorldViewModel World { get; }

    /// <summary>The asset inspector content.</summary>
    public AssetInspectorViewModel Asset { get; }

    /// <summary>Whether the asset view is shown (else the entity view).</summary>
    [ObservableProperty]
    public partial bool ShowAsset { get; private set; }

    // An entity became selected: it wins the inspector, so clear any asset selection (which flips ShowAsset off).
    private void OnWorldSelectionChanged()
    {
        if (_worldSelection.PrimaryId is not null)
            _assetSelection.Clear();
    }

    // The asset selection changed: when set, it wins (clear the entity selection and load it); when cleared, fall
    // back to the entity view.
    private void OnAssetSelectionChanged()
    {
        if (_assetSelection.HasSelection)
        {
            _worldSelection.Clear();
            ShowAsset = true;
            Asset.LoadAsync(_assetSelection.Current).FireAndForget();
        }
        else
        {
            ShowAsset = false;
            Asset.Clear();
        }
    }
}
