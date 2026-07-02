using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Logging;
using Toybox.Studio.Project;
using Toybox.Studio.Project.Assets;
using Toybox.Studio.Utils;
using Toybox.Studio.PropertyGrid;

namespace Toybox.Studio.EntityInspector;

/// <summary>
/// The Inspector's view of a selected ASSET (the counterpart to the entity view). Loads the asset's editable
/// body (an <c>asset.describe</c> snapshot) into the same type-driven property grid the components use, but edits
/// are BUFFERED: a change marks the panel dirty and only the Save button writes the file (<c>asset.save</c>);
/// Cancel reloads the on-disk values. Only property-bearing assets (materials, material instances) describe;
/// other kinds show an informational message.
/// </summary>
public sealed partial class AssetInspectorViewModel : ObservableObject
{
    private readonly AssetFactory _factory;
    private readonly Logger _log;

    private Asset? _asset;
    private AssetHandle _handle = AssetHandle.None;

    // The typed payload whose DirtyChanged the panel is currently mirroring, so its subscription can be dropped on
    // reload/clear. A typed asset's rows self-commit through their ReflectedAccessor and dirty the payload directly
    // (OnFieldEdited); the panel tracks that through this hook rather than each row calling MarkDirty by hand.
    private AssetData? _dirtyData;

    // For a Material asset, all of its top-level rows (built once) and the "type" dropdown they filter by, so a
    // change of render category re-shows only the relevant rows without reloading. Null for any other asset.
    private IReadOnlyList<PropertyViewModel> _materialRows = [];
    private DropdownPropertyViewModel? _materialTypeRow;

    public AssetInspectorViewModel(AssetFactory factory, Logger log)
    {
        _factory = factory;
        _log = log;
    }

    /// <summary>The grid rows for the asset's editable properties (empty when the asset has none / can't load).</summary>
    public ObservableCollection<PropertyViewModel> Properties { get; } = [];

    /// <summary>The asset's display name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    public partial string Title { get; private set; } = "";

    /// <summary>The asset's type pill (e.g. "MAT").</summary>
    [ObservableProperty]
    public partial string Subtitle { get; private set; } = "";

    /// <summary>Whether the loaded asset has editable properties (drives grid-vs-message).</summary>
    [ObservableProperty]
    public partial bool IsEditable { get; private set; }

    /// <summary>Whether there are buffered, unsaved edits (drives the Save/Cancel footer and the '*').</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    public partial bool IsDirty { get; private set; }

    /// <summary>The message shown when the asset isn't editable (loading, not describable, or load failed).</summary>
    [ObservableProperty]
    public partial string Status { get; private set; } = "";

    /// <summary>The header label — the asset name with a trailing '*' while there are unsaved edits.</summary>
    public string HeaderText => IsDirty ? $"{Title} *" : Title;

    /// <summary>Loads an asset for editing (replacing any current one). Buffered: nothing persists until Save.</summary>
    public async Task LoadAsync(AssetHandle handle)
    {
        _handle = handle;
        _asset = null;
        Properties.Clear();
        IsDirty = false;
        IsEditable = false;
        Title = handle.Name;
        Subtitle = handle.Type.ToUpperInvariant();
        Status = "Loading…";

        var result = await _factory.For(handle).LoadAsync(CancellationToken.None).ContinueOnSameContext();

        // A newer selection may have superseded this load while it was in flight.
        if (_handle.Id != handle.Id || !string.Equals(_handle.Path, handle.Path, StringComparison.OrdinalIgnoreCase))
            return;

        if (result is not { Success: true, Value: { } asset })
        {
            Status = "This asset can't be edited in the inspector.";
            return;
        }

        _asset = asset;
        BuildProperties();
    }

    /// <summary>Clears the panel (no asset selected).</summary>
    public void Clear()
    {
        DetachMaterialType();
        DetachDirty();
        _handle = AssetHandle.None;
        _asset = null;
        Properties.Clear();
        IsDirty = false;
        IsEditable = false;
        Title = "";
        Subtitle = "";
        Status = "";
    }

    private void BuildProperties()
    {
        DetachMaterialType();
        DetachDirty();
        Properties.Clear();
        _materialRows = [];
        if (_asset is not { } asset)
            return;

        // A typed payload's rows self-commit and dirty the payload; mirror its dirty state onto the panel.
        if (asset.Data is { } typedData)
        {
            _dirtyData = typedData;
            typedData.DirtyChanged += OnDataDirtyChanged;
        }

        // Source the grid from the TYPED C# payload by reflection (ReflectionWalk) — never a JSON describe round-trip
        // — when the asset has a typed model; assets without one (none editable today) fall back to the describe
        // body. The typed rows self-commit through their ReflectedAccessor, which folds the edit into the typed
        // field (CollectSynced persists it on Save) and dirties the asset via OnFieldEdited; the panel mirrors that
        // dirty state through DirtyChanged (wired in LoadAsync).
        var data = asset.Data;
        var typed = data is not null && ReflectionWalk.HasModel(data.GetType());

        // A material instance edits against its base material's slots (Base picker + base-locked override
        // sections), exactly like the material_instance component — not the generic add/remove grid its
        // overrides would otherwise render as. Buffered against the typed overrides field (CommitWire + dirty).
        if (data is MaterialInstance instance && TryBuildMaterialInstance(instance))
        {
            IsEditable = Properties.Count > 0;
            Status = IsEditable ? "" : "This asset has no editable properties.";
            return;
        }

        var pairs = typed
            ? ReflectionWalk.Walk(data!)
            : asset.Body is { } body
                ? JsonDescriptorReader.Read(body, Commit)
                : [];
        var rows = pairs.Select(pair => BuildRow(pair.Descriptor, pair.Accessor, data)).ToList();

        // A material's editor respects its render category: the type-specific fields (the raster render config)
        // are hidden for a material that doesn't apply to them. Other assets show every row as-is.
        if (_asset?.Data is Material
            && rows.FirstOrDefault(row => row.RawName == "type") is DropdownPropertyViewModel typeRow)
        {
            _materialRows = rows;
            _materialTypeRow = typeRow;
            typeRow.PropertyChanged += OnMaterialTypeChanged;
            ApplyMaterialFilter();
        }
        else
        {
            foreach (var row in rows)
                Properties.Add(row);
        }

        IsEditable = Properties.Count > 0;
        Status = IsEditable ? "" : "This asset has no editable properties.";
    }

    // Builds one top-level asset row. A base Material's parameter/texture bindings aren't a generic array: each
    // is where the material's shape (name / value type / default) is DEFINED, so they get the dedicated
    // definition editors (name + type + default, add/remove). Every other field takes the type-driven widget.
    private PropertyViewModel BuildRow(
        Toybox.Studio.PropertyGrid.PropertyDescriptor descriptor, IValueAccessor accessor, AssetData? data)
    {
        if (data is Material material)
        {
            if (descriptor.Name == "parameters")
                return new MaterialParametersEditorViewModel(
                    material.Parameters.Values, depth: 0, CommitField("parameters", material));
            if (descriptor.Name == "textures")
                return new MaterialTexturesEditorViewModel(
                    material.Textures.Values, depth: 0, CommitField("textures", material));
        }

        return PropertyViewModelFactory.Create(descriptor, accessor);
    }

    // Builds the material-instance editor for a .mti ASSET: the "Base" material picker plus the base-aware
    // override sections, reusing the same wiring as the component / nested-field editor
    // (MaterialInstancePropertyViewModel.Build). Both editors are JSON-shaped, so they bind to a bare JSON view of
    // the typed model (EngineSyncValue.WriteBare — NOT a describe emitter) and commit their whole field back
    // through the typed model via CommitWire, which reconstructs the typed field (ReadBare) and dirties the asset.
    private bool TryBuildMaterialInstance(MaterialInstance data)
    {
        if (AssetGridServices.Assets is null)
            return false;

        var materialPair = BareField("material", EngineTypes.Handle, data.Material);
        var overridesPair = BareField("overrides", EngineTypes.Object, data.Overrides);

        var (materialRow, overridesRow) = MaterialInstancePropertyViewModel.Build(
            materialPair,
            overridesPair,
            () => MaterialInstancePropertyViewModel.ReadHandleId(materialPair.Accessor),
            CommitBare("material", materialPair.Accessor, data),
            CommitBare("overrides", overridesPair.Accessor, data),
            depth: 0);

        Properties.Add(materialRow);
        Properties.Add(overridesRow);
        return true;
    }

    // A (descriptor, JSON accessor) pair for one typed field rendered as a bare JSON view: the JSON editors mutate
    // the live token in place, and CommitBare folds the whole field back into the typed model. The MaterialInstance
    // editors carry the material's "Base" label, so the material row picks it up here.
    private static (Toybox.Studio.PropertyGrid.PropertyDescriptor Descriptor, IValueAccessor Accessor) BareField(
        string wire, string type, object? value)
    {
        var descriptor = new Toybox.Studio.PropertyGrid.PropertyDescriptor
        {
            Name = wire,
            Type = type,
            Choices = wire == "material" ? ["mat"] : null,
            Label = wire == "material" ? "Base" : null,
        };
        var token = EngineSyncValue.WriteBare(value);
        return (descriptor, new JsonAccessor(token, type, null));
    }

    // Commits one whole material-instance field back into the typed model from the live JSON view the editor
    // mutated (read straight off its accessor), then flags the panel dirty.
    private Action CommitBare(string wire, IValueAccessor accessor, AssetData data) => () =>
    {
        if (accessor.Get() is JToken token)
            data.CommitWire(wire, token);
        MarkDirty();
    };

    // The buffered commit for one whole material field re-serialised as a lean token: apply it to the typed
    // payload (so Save folds the real edited values back) and flag the panel dirty.
    private Action<JToken> CommitField(string wire, AssetData data) => token =>
    {
        data.CommitWire(wire, token);
        MarkDirty();
    };

    private void OnMaterialTypeChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(DropdownPropertyViewModel.Value))
            ApplyMaterialFilter();
    }

    // Rebuilds the visible rows for the current render category, reusing the row view-models (so any expansion
    // and in-progress edits survive a type switch) — only which of them are present in the grid changes.
    private void ApplyMaterialFilter()
    {
        if (_materialTypeRow is null)
            return;

        var index = _materialTypeRow.Choices.ToList().IndexOf(_materialTypeRow.Value);
        var type = index >= 0 ? (MaterialType)index : MaterialType.Raster;

        Properties.Clear();
        foreach (var row in _materialRows)
            if (MaterialFields.IsVisible(type, row.RawName))
                Properties.Add(row);
    }

    private void DetachMaterialType()
    {
        if (_materialTypeRow is not null)
            _materialTypeRow.PropertyChanged -= OnMaterialTypeChanged;
        _materialTypeRow = null;
    }

    // The commit for one top-level property on the JSON (untyped) fallback path: the leaf mutated the body token
    // in place, so the buffered Save writes it back untouched — the commit only flags the panel dirty. (A typed
    // asset's rows self-commit through their ReflectedAccessor and dirty via DirtyChanged, not this.)
    private Action Commit(string wire) => MarkDirty;

    private void MarkDirty() => IsDirty = true;

    // Mirrors the typed payload's dirty state onto the panel (its rows dirty it directly through their reflected
    // commits). ClearDirty on Save re-arms this so the next edit re-flags.
    private void OnDataDirtyChanged(bool dirty) => Dispatch.To(DispatchContext.UI, () => IsDirty = dirty);

    private void DetachDirty()
    {
        if (_dirtyData is not null)
            _dirtyData.DirtyChanged -= OnDataDirtyChanged;
        _dirtyData = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_asset is null || !IsDirty)
            return;

        var result = await _asset.SaveAsync(CancellationToken.None).ContinueOnSameContext();
        if (result.Success)
        {
            // Clear the payload's dirty flag too, so its DirtyChanged re-arms and the next edit re-flags the panel
            // (DirtyChanged only fires on a state CHANGE).
            _asset.Data?.ClearDirty();
            IsDirty = false;
            _log.Info($"Saved asset '{Title}'.");
        }
        else
        {
            await Popups.ShowErrorAsync("Couldn't save asset", result.Error ?? "Unknown error.")
                .ContinueOnSameContext();
        }
    }

    [RelayCommand]
    private Task CancelAsync() => _handle.IsNone ? Task.CompletedTask : LoadAsync(_handle);
}
