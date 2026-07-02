using Toybox.Studio.Utils;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Dialogs;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Worlds;
using Toybox.Studio.Project;
using Toybox.Studio.PropertyGrid;

namespace Toybox.Studio.Ecs;

/// <summary>
/// One component on an entity, rendered as a type-driven property grid. Reusable (not inspector-specific)
/// and self-editing: each top-level property pushes its own change to the engine via the reflector, and on
/// rejection it surfaces the engine's message and asks the world to re-sync to engine truth.
/// </summary>
public sealed partial class ComponentViewModel : ObservableObject
{
    private readonly Component _component;
    private readonly JObject _raw;

    // True when this component's grid is sourced from its TYPED C# model via ReflectionWalk (which binds each
    // editor straight to the reflected field) rather than the engine's describe JSON. Gated on model completeness
    // (UsesEngineSync); the value editors and enable toggle are shared, only the value source + commit differ.
    private readonly bool _synced;
    private readonly Func<Task> _resync;

    // Top-level property name → its view-model, for routing the per-property modified-state refresh.
    private readonly Dictionary<string, PropertyViewModel> _resettable = [];

    private const string MaterialInstanceName = "material_instance";
    private const string RendererName = "renderer";
    private const string EnabledProperty = "is_enabled";

    // The base-Component framework fields every component's describe carries (id + is_enabled). They're not
    // modeled as typed subclass fields, so the completeness gate must not require them to be covered.
    private const string IdProperty = "id";

    // The live bool value token inside Raw for the base-component is_enabled flag. The flag is [[hidden]] so
    // it never becomes a grid row; the inspector exposes it as the toggle in the component header instead.
    // Null only for the (legacy) case of a component whose payload carries no is_enabled field.
    private readonly JValue? _enabled;

    private bool _isEnabled = true;

    public ComponentViewModel(
        Component component,
        Func<Task> resync)
    {
        _component = component;
        _resync = resync;
        Name = component.Name;
        DisplayName = NameHumanizer.Humanize(component.Name);
        // The component header icon is editor-defined: the [Icon] attribute on the component type (inherited from
        // a base type when the subclass declares none), resolved to a palette colour. No engine icon fallback.
        var icon = component.GetType().GetCustomAttribute<IconAttribute>();
        Icon = icon?.Name ?? Icon.None;
        IconColor = icon?.Color.ToColor();
        Properties = [];

        // Source the grid from the TYPED C# model when it covers the whole component (each row binds straight to a
        // reflected field via ReflectionWalk, and the row's accessor owns its own engine push), else from the
        // component's engine describe JSON (which builds the reflect.set payload per top-level property itself).
        // The value editors and the enable toggle are shared; only the value source + commit differ. is_enabled
        // isn't a typed field, so the toggle always reads/commits it through the component's Raw describe body.
        _synced = UsesEngineSync(component);
        _raw = component.Raw;
        // A reflected component's rows self-commit (their ReflectedAccessor pushes through the generated setter), so
        // the typed branch needs no per-property commit here; the describe branch binds one commit per top-level
        // property (the engine's reflect.set is top-level-property granular) that builds the reflect.set payload.
        var properties = _synced
            ? ReflectionWalk.Walk(component)
            : JsonDescriptorReader.Read(_raw, CommitFor);

        _enabled = ReadEnabledToken(_raw);
        _isEnabled = _enabled?.Value<bool>() ?? true;

        // The special composite editors apply on BOTH paths: material_instance / renderer now source their values
        // from the typed model (they qualify for _synced), but still get their bespoke base-aware editors. The
        // editors are path-agnostic — they commit through each property's own accessor (a reflected setter push on
        // the typed path, a describe reflect.set on the JSON path). The single-struct flatten runs on both paths.

        // A material instance is not a generic property bag: its "overrides" are edited against the base
        // material's slots (fetched live), so it gets a dedicated, base-aware editor instead of the grid.
        if (component.Name == MaterialInstanceName && TryBuildMaterialInstance(properties))
            return;

        // The Renderer's "materials" aren't a generic numbered list either: each entry overrides one of the
        // model's hard material slots, so it gets a model-aware editor that fetches those slots (named from the
        // source file at import) live and labels a picker per slot.
        if (component.Name == RendererName && TryBuildRenderer(properties))
            return;

        // A component whose entire payload is a single STRUCT repeats itself: the component header and that lone
        // child's header say the same thing (script_container -> Scripts, transform -> Transform). Flatten it —
        // promote the child's members to the top level so the header alone names the group. Every edit still
        // routes through the one top-level property the engine round-trips, so the value path is unchanged; the
        // promoted members are nested fields and so (like any nested field) carry no individual reset —
        // reflect.reset is whole-property granular. A single ARRAY is NOT flattened: it must keep its own list
        // row so it stays addable/removable (promoting its elements to the root would lose the "+" affordance).
        var single = properties.Count == 1 ? properties[0] : default;
        if (single.Descriptor is { HasChildren: true, Type: not EngineTypes.Array } singleDescriptor)
        {
            foreach (var child in singleDescriptor.Children)
            {
                // A read-only single-struct withholds the commit from every promoted member (the factory also
                // disables their controls), matching the old flatten behaviour.
                var childAccessor = single.Accessor.Member(child);
                if (singleDescriptor.ReadOnly)
                    childAccessor = new ReadOnlyAccessor(childAccessor);
                var viewModel = PropertyViewModelFactory.Create(child, childAccessor);
                // The promoted members all live under the one top-level property; reset is whole-property
                // granular, so each member's indicator resets that property (i.e. the whole flattened struct).
                if (!singleDescriptor.ReadOnly)
                    viewModel.ResetToDefault = () => OnPropertyReset(singleDescriptor.Name);
                Properties.Add(viewModel);
            }

            return;
        }

        foreach (var (descriptor, accessor) in properties)
        {
            var property = descriptor.Name;
            var viewModel = PropertyViewModelFactory.Create(descriptor, accessor);
            if (!descriptor.ReadOnly)
            {
                viewModel.ResetToDefault = () => OnPropertyReset(property);
                _resettable[property] = viewModel;
            }

            Properties.Add(viewModel);
        }
    }

    // The per-top-level-property commit a describe-sourced component's accessor subtree shares: build the
    // reflect.set payload from the leaf's live token (OnPropertyEdited). Only the describe path uses this — a
    // reflected component's rows self-commit through their ReflectedAccessor, so it passes no commit here.
    private Action CommitFor(string property) => () => OnPropertyEdited(property);

    public string Name { get; }

    /// <summary>The component's display label — its type name humanized to read like the properties below.</summary>
    public string DisplayName { get; }

    public Icon Icon { get; }

    public Avalonia.Media.Color? IconColor { get; }

    public ObservableCollection<PropertyViewModel> Properties { get; }

    /// <summary>
    /// True when this component carries the base <c>is_enabled</c> flag, so the header shows its enable
    /// toggle. Every engine component derives from <c>Component</c> and so has it; false only guards the
    /// degenerate case of a payload without the field.
    /// </summary>
    public bool HasEnableToggle => _enabled is not null;

    /// <summary>
    /// Whether this component is active, mirrored from the base <c>is_enabled</c> field. Toggling mutates the
    /// backing JSON in place and pushes the change through the same per-property round-trip an ordinary edit
    /// uses — the flag is hidden from the grid, so the header toggle is its only editor.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!SetProperty(ref _isEnabled, value) || _enabled is null)
                return;

            _enabled.Value = value;
            OnPropertyEdited(EnabledProperty);
        }
    }

    /// <summary>The inspector search pushed in by the host; the component's grid filters its rows by it.</summary>
    [ObservableProperty]
    public partial string? Filter { get; set; }

    /// <summary>
    /// Pushes a fresh snapshot of the same component into the existing property rows in place, without
    /// rebuilding them — so tracking a running game's live values keeps the grid's controls (and any
    /// in-progress edit) rather than tearing them down every tick. Returns false when the component's shape
    /// no longer matches these rows (a property added/removed/retyped, or a value of a type that can't update
    /// in place actually moved), in which case the host rebuilds the component instead.
    /// </summary>
    public bool SyncFrom(Component snapshot)
    {
        // Track the live enable flag too (it's not a grid row, so it isn't covered by the row zip below).
        SyncEnabled(snapshot);

        // The special composite editors (material_instance / renderer) build a bespoke row shape that doesn't line
        // up with the raw walk pairs, so a positional zip would mis-match. Force a clean rebuild for them rather
        // than risk zipping the wrong rows — these components don't need per-tick in-place tracking.
        if (Name is MaterialInstanceName or RendererName)
            return false;

        // Re-derive the fresh (descriptor, accessor) pairs from the snapshot the same way the constructor did: a
        // reflected component walks the typed model, a describe-sourced one reads its describe JSON. Then flatten
        // identically (EffectivePairs) so the row zip lines up.
        var pairs = EffectivePairs(
            _synced ? ReflectionWalk.Walk(snapshot) : JsonDescriptorReader.Read(snapshot.Raw));
        if (pairs.Count != Properties.Count)
            return false;

        var synced = true;
        for (var index = 0; index < Properties.Count; index++)
        {
            if (!string.Equals(Properties[index].RawName, pairs[index].Descriptor.Name, StringComparison.Ordinal))
                return false;
            synced &= Properties[index].Sync(pairs[index].Descriptor, pairs[index].Accessor);
        }

        return synced;
    }

    /// <summary>
    /// Removes this whole component from the entity (the header's "✕"), then re-syncs so the inspector drops
    /// its section. Surfaces the engine's message on failure.
    /// </summary>
    [RelayCommand]
    private async Task RemoveComponentAsync()
    {
        var result = await _component.RemoveAsync(CancellationToken.None).ContinueOnSameContext();
        if (result.Success)
            await _resync().ContinueOnSameContext();
        else
            await Popups.ShowErrorAsync("Couldn't remove component", result.Error!).ContinueOnSameContext();
    }

    // Builds the material-instance editor: a "Base" material picker plus a base-aware override editor that
    // shows the referenced material's parameter/texture slots. Reuses the same wiring as the nested/list
    // editor (MaterialInstancePropertyViewModel.Build) but, at the component's top level, flattens the pair
    // into the component's own rows (so the component header alone names the group) and routes the base and
    // overrides to their own reflect.set properties — each is a top-level component property here, whereas a
    // nested instance commits both together. Returns false (falling back to the generic grid) if the component
    // isn't shaped as expected.
    private bool TryBuildMaterialInstance(
        IReadOnlyList<(PropertyDescriptor Descriptor, IValueAccessor Accessor)> properties)
    {
        // The base-aware editor loads the referenced material's slots through the asset facade (wired at
        // startup alongside the catalog); a missing catalog just means falling back to the generic grid.
        if (AssetGridServices.Assets is null)
            return false;

        var material = Field(properties, "material");
        var overrides = Field(properties, "overrides");
        if (material.Descriptor is null || overrides.Descriptor is null)
            return false;

        // Path-agnostic: the Base picker and its commit work on both paths (a JSON/reflected accessor over an
        // AssetHandle both Get() an AssetHandle and Commit() through their own owner). The overrides editor is
        // JSON-shaped, so on the TYPED path (a reflected MaterialOverrides) we hand it a bare JSON VIEW and write
        // the edit back through the reflected accessor (the shared bridge); on the JSON path the live token
        // accessor drives it directly. Detected the same way MaterialInstancePropertyViewModel does.
        var (overridesAccessor, commitOverrides) = overrides.Accessor.Get() is JObject
            ? (overrides.Accessor, overrides.Accessor.Commit)
            : MaterialInstancePropertyViewModel.TypedOverridesBridge(overrides.Accessor) switch
            {
                var bridge => ((IValueAccessor)bridge.View, bridge.Commit),
            };

        var (materialRow, overridesRow) = MaterialInstancePropertyViewModel.Build(
            material,
            (overrides.Descriptor, overridesAccessor),
            () => MaterialInstancePropertyViewModel.ReadHandleId(material.Accessor),
            material.Accessor.Commit,
            commitOverrides,
            depth: 0);

        if (!material.Descriptor.ReadOnly)
        {
            materialRow.ResetToDefault = () => OnPropertyReset("material");
            _resettable["material"] = materialRow;
        }

        Properties.Add(materialRow);
        Properties.Add(overridesRow);
        return true;
    }

    // Builds the Renderer's model-aware editor: the Model picker plus a per-slot materials editor whose rows are
    // the model's hard material slots (named from the source file, fetched live), each an asset picker that
    // overrides that slot. Picking a new model relabels the slots against it. The two route to their own
    // reflect.set properties (model / materials), each a top-level component property. Returns false (falling
    // back to the generic grid) when the catalog is unavailable or the component isn't shaped as expected.
    private bool TryBuildRenderer(
        IReadOnlyList<(PropertyDescriptor Descriptor, IValueAccessor Accessor)> properties)
    {
        if (AssetGridServices.Assets is not { } catalog)
            return false;

        var model = Field(properties, "model");
        var materialsField = Field(properties, "materials");
        if (model.Descriptor is null || materialsField.Descriptor is null)
            return false;

        // Path-agnostic: the materials editor commits through its OWN accessor (a reflected list push on the typed
        // path; the passed describe commit on the JSON path — equivalent to materialsField.Accessor.Commit there).
        var materials = new RendererMaterialsViewModel(
            materialsField.Descriptor,
            materialsField.Accessor,
            catalog,
            MaterialInstancePropertyViewModel.ReadHandleId(model.Accessor),
            materialsField.Accessor.Commit,
            depth: 0);

        // Picking a new model reloads the slot rows against it — the slots, and their names, come from the model.
        // The model handle commits through its own accessor uniformly (reflected setter push or describe reflect.set).
        var modelRow = PropertyViewModelFactory.Create(model.Descriptor, model.Accessor);
        modelRow.CommitChanges = () =>
        {
            model.Accessor.Commit();
            materials.ReloadAsync(MaterialInstancePropertyViewModel.ReadHandleId(model.Accessor)).FireAndForget();
        };

        if (!model.Descriptor.ReadOnly)
        {
            modelRow.ResetToDefault = () => OnPropertyReset("model");
            _resettable["model"] = modelRow;
        }

        Properties.Add(modelRow);
        Properties.Add(materials);
        return true;
    }

    // The (descriptor, accessor) pair for a named top-level property, or (null, …) when absent.
    private static (PropertyDescriptor Descriptor, IValueAccessor Accessor) Field(
        IReadOnlyList<(PropertyDescriptor Descriptor, IValueAccessor Accessor)> properties, string name)
    {
        foreach (var pair in properties)
            if (pair.Descriptor.Name == name)
                return pair;
        return default;
    }

    // Whether to source this component's grid from its typed reflected model. Used only when the typed model
    // covers EVERY editable describe field (else reflection would silently drop the unmodeled ones), so an
    // incomplete model safely stays on the describe path and auto-switches over once it's completed — no per-type
    // allowlist. The special composite editors (material instance / renderer) are path-agnostic, so they run on
    // the reflected path once their typed models cover the describe — no carve-out here.
    private static bool UsesEngineSync(Component component)
    {
        var wires = component.SyncedWires;
        if (wires.Count == 0)
            return false;

        var modeled = new HashSet<string>(wires, StringComparer.Ordinal);
        foreach (var property in component.Raw.Properties())
            if (property.Name is not (EnabledProperty or IdProperty) && !modeled.Contains(property.Name))
                return false;

        return true;
    }

    // Pulls the live bool value token for is_enabled out of a component's raw JSON. The field arrives as a
    // typed/attributed wrapper ({ ..., "value": true }); a bare value is tolerated for resilience.
    private static JValue? ReadEnabledToken(JObject raw) => raw[EnabledProperty].Unwrap() as JValue;

    // Mirrors the latest is_enabled value into both the canonical backing token (kept in Raw) and the bound
    // property, without re-committing — the value already came from the engine. Used by the live-value sync.
    private void SyncEnabled(Component snapshot)
    {
        var incoming = ReadEnabledToken(snapshot.Raw)?.Value<bool>() ?? true;
        if (_enabled is not null)
            _enabled.Value = incoming;
        SetProperty(ref _isEnabled, incoming, nameof(IsEnabled));
    }

    // The top-level property pairs the grid actually shows: a single-struct component is flattened to its
    // child's members (see the constructor), so the row set is that child's members; otherwise it's the
    // component's own properties. Kept in step with the constructor so SyncFrom zips against the same rows.
    private IReadOnlyList<(PropertyDescriptor Descriptor, IValueAccessor Accessor)> EffectivePairs(
        IReadOnlyList<(PropertyDescriptor Descriptor, IValueAccessor Accessor)> properties)
    {
        // Match the constructor's flatten rule EXACTLY: a single STRUCT child is flattened to its members, but
        // a single ARRAY keeps its own list row. If these disagreed (e.g. a lone-array component), SyncFrom
        // would zip against the wrong row set and force a full inspector rebuild every tick.
        var single = properties.Count == 1 ? properties[0] : default;
        if (single.Descriptor is { HasChildren: true, Type: not EngineTypes.Array } singleDescriptor)
            return singleDescriptor.Children
                .Select(child => (child, single.Accessor.Member(child)))
                .ToList();

        return properties;
    }

    private async void OnPropertyEdited(string property)
    {
        // The leaf mutated the live token inside Raw; read the property's current bare value back out.
        var bare = _raw[property].Unwrap();
        if (bare is null)
            return;

        var result = await _component
            .SetPropertyAsync(property, bare, CancellationToken.None)
            .ContinueOnSameContext();
        if (result.Success)
        {
            // An authored value is "set" by definition; mark it optimistically so the indicator appears at
            // once without a second round-trip per edit (which would double traffic during a scrub-drag).
            // The authoritative isDefault check still runs on the next selection / world refresh.
            if (_resettable.TryGetValue(property, out var viewModel))
                viewModel.IsModified = true;
            return;
        }

        await Popups.ShowErrorAsync(
            "Couldn't apply change",
            $"The engine rejected the edit to '{Name}.{property}':\n\n{result.Error}")
            .ContinueOnSameContext();
        await _resync().ContinueOnSameContext();
    }

    private async void OnPropertyReset(string property)
    {
        var result = await _component
            .ResetPropertyAsync(property, CancellationToken.None)
            .ContinueOnSameContext();
        if (!result.Success)
        {
            await Popups.ShowErrorAsync(
                "Couldn't reset",
                $"The engine could not reset '{Name}.{property}':\n\n{result.Error}")
                .ContinueOnSameContext();
        }

        // Re-sync either way so the grid reflects engine truth (the restored value on success).
        await _resync().ContinueOnSameContext();
    }
}
