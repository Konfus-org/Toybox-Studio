using Toybox.Studio.Utils;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.Dialogs;
using Toybox.Studio.Models.Ecs;
using Toybox.Studio.Services.EngineApi;
using Toybox.Studio.Widgets.PropertyGrid;

namespace Toybox.Studio.Widgets.Ecs;

/// <summary>
/// One component on an entity, rendered as a type-driven property grid. Reusable (not inspector-specific)
/// and self-editing: each top-level property pushes its own change to the engine via the reflector, and on
/// rejection it surfaces the engine's message and asks the world to re-sync to engine truth.
/// </summary>
public sealed partial class ComponentViewModel : ObservableObject
{
    private readonly ulong _entityId;
    private readonly JObject _raw;
    private readonly EngineRpc _engine;
    private readonly Func<Task> _resync;

    // Invoked after any edit/reset the engine accepts; the host uses it to mark the world dirty and to guard
    // the live play-mode pull from clobbering a value the user just changed.
    private readonly Action _onEdited;

    // Top-level property name → its view-model, for routing the per-property modified-state refresh.
    private readonly Dictionary<string, PropertyViewModel> _resettable = [];

    private const string MaterialInstanceName = "material_instance";
    private const string EnabledProperty = "is_enabled";

    // The live bool value token inside Raw for the base-component is_enabled flag. The flag is [[hidden]] so
    // it never becomes a grid row; the inspector exposes it as the toggle in the component header instead.
    // Null only for the (legacy) case of a component whose payload carries no is_enabled field.
    private readonly JValue? _enabled;

    public ComponentViewModel(
        ulong entityId,
        Component component,
        EngineRpc engine,
        Func<Task> resync,
        Action onEdited)
    {
        _entityId = entityId;
        _raw = component.Raw;
        _engine = engine;
        _resync = resync;
        _onEdited = onEdited;
        Name = component.Name;
        DisplayName = NameHumanizer.Humanize(component.Name);
        Icon = component.Icon;
        IconColor = component.IconColor;
        Properties = [];

        _enabled = ReadEnabledToken(_raw);
        _isEnabled = _enabled?.Value<bool>() ?? true;

        // A material instance is not a generic property bag: its "overrides" are edited against the base
        // material's slots (fetched live), so it gets a dedicated, base-aware editor instead of the grid.
        if (component.Name == MaterialInstanceName && TryBuildMaterialInstance(component))
            return;

        // A component whose entire payload is a single STRUCT repeats itself: the component header and that lone
        // child's header say the same thing (script_container -> Scripts, transform -> Transform). Flatten it —
        // promote the child's members to the top level so the header alone names the group. Every edit still
        // routes through the one top-level property the engine round-trips, so the value path is unchanged; the
        // promoted members are nested fields and so (like any nested field) carry no individual reset —
        // reflect.reset is whole-property granular. A single ARRAY is NOT flattened: it must keep its own list
        // row so it stays addable/removable (promoting its elements to the root would lose the "+" affordance).
        var single = component.Properties.Count == 1 ? component.Properties[0] : null;
        if (single is { HasChildren: true, Type: not "array" })
        {
            var commit = single.ReadOnly ? (Action?)null : () => OnPropertyEdited(single.Name);
            foreach (var child in single.Children)
            {
                var viewModel = PropertyViewModelFactory.Create(child, commit);
                // The promoted members all live under the one top-level property; reset is whole-property
                // granular, so each member's indicator resets that property (i.e. the whole flattened struct).
                if (!single.ReadOnly)
                    viewModel.ResetToDefault = () => OnPropertyReset(single.Name);
                Properties.Add(viewModel);
            }

            return;
        }

        // Each top-level property gets a commit/reset bound to its name — the engine's reflect.set/reset is
        // top-level-property granular, so an edit to any leaf within it pushes just that one property.
        foreach (var node in component.Properties)
        {
            var property = node.Name;
            var viewModel = PropertyViewModelFactory.Create(node, () => OnPropertyEdited(property));
            if (!node.ReadOnly)
            {
                viewModel.ResetToDefault = () => OnPropertyReset(property);
                _resettable[property] = viewModel;
            }

            Properties.Add(viewModel);
        }
    }

    // Builds the material-instance editor: a "Base" material picker plus a base-aware override editor that
    // shows the referenced material's parameter/texture slots. Reuses the same wiring as the nested/list
    // editor (MaterialInstancePropertyViewModel.Build) but, at the component's top level, flattens the pair
    // into the component's own rows (so the component header alone names the group) and routes the base and
    // overrides to their own reflect.set properties — each is a top-level component property here, whereas a
    // nested instance commits both together. Returns false (falling back to the generic grid) if the component
    // isn't shaped as expected.
    private bool TryBuildMaterialInstance(Component component)
    {
        var materialNode = component.Properties.FirstOrDefault(node => node.Name == "material");
        var overridesNode = component.Properties.FirstOrDefault(node => node.Name == "overrides");
        if (materialNode is null || overridesNode is null)
            return false;

        var (material, overrides) = MaterialInstancePropertyViewModel.Build(
            materialNode,
            overridesNode,
            _engine,
            () => MaterialInstancePropertyViewModel.ReadHandleId(_raw["material"]),
            () => OnPropertyEdited("material"),
            () => OnPropertyEdited("overrides"),
            depth: 0);

        if (!materialNode.ReadOnly)
        {
            material.ResetToDefault = () => OnPropertyReset("material");
            _resettable["material"] = material;
        }

        Properties.Add(material);
        Properties.Add(overrides);
        return true;
    }

    public string Name { get; }

    /// <summary>The component's display label — its type name humanized to read like the properties below.</summary>
    public string DisplayName { get; }

    public string? Icon { get; }

    public string? IconColor { get; }

    public ObservableCollection<PropertyViewModel> Properties { get; }

    /// <summary>
    /// True when this component carries the base <c>is_enabled</c> flag, so the header shows its enable
    /// toggle. Every engine component derives from <c>Component</c> and so has it; false only guards the
    /// degenerate case of a payload without the field.
    /// </summary>
    public bool HasEnableToggle => _enabled is not null;

    private bool _isEnabled = true;

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

    // Pulls the live bool value token for is_enabled out of a component's raw JSON. The field arrives as a
    // typed/attributed wrapper ({ ..., "value": true }); a bare value is tolerated for resilience.
    private static JValue? ReadEnabledToken(JObject raw)
    {
        var token = raw[EnabledProperty];
        if (token is JObject wrapper && wrapper["value"] is JValue wrapped)
            return wrapped;
        return token as JValue;
    }

    /// <summary>
    /// Pushes a fresh snapshot of the same component into the existing property rows in place, without
    /// rebuilding them — so tracking a running game's live values keeps the grid's controls (and any
    /// in-progress edit) rather than tearing them down every tick. Returns false when the component's shape
    /// no longer matches these rows (a property added/removed/retyped, or a value of a type that can't update
    /// in place actually moved), in which case the host rebuilds the component instead.
    /// </summary>
    public bool SyncFrom(Component component)
    {
        // Track the live enable flag too (it's not a grid row, so it isn't covered by the row zip below).
        SyncEnabled(component);

        var nodes = EffectiveNodes(component);
        if (nodes.Count != Properties.Count)
            return false;

        var synced = true;
        for (var index = 0; index < Properties.Count; index++)
        {
            if (!string.Equals(Properties[index].RawName, nodes[index].Name, StringComparison.Ordinal))
                return false;
            synced &= Properties[index].Sync(nodes[index]);
        }

        return synced;
    }

    // Mirrors the latest is_enabled value into both the canonical backing token (kept in Raw) and the bound
    // property, without re-committing — the value already came from the engine. Used by the live-value sync.
    private void SyncEnabled(Component component)
    {
        var incoming = ReadEnabledToken(component.Raw)?.Value<bool>() ?? true;
        if (_enabled is not null)
            _enabled.Value = incoming;
        SetProperty(ref _isEnabled, incoming, nameof(IsEnabled));
    }

    // The top-level property nodes the grid actually shows: a single-struct component is flattened to its
    // child's members (see the constructor), so the row set is that child's children; otherwise it's the
    // component's own properties. Kept in step with the constructor so SyncFrom zips against the same rows.
    private static IReadOnlyList<PropertyNode> EffectiveNodes(Component component)
    {
        var single = component.Properties.Count == 1 ? component.Properties[0] : null;
        return single is { HasChildren: true } ? single.Children : component.Properties;
    }

    /// <summary>The inspector search pushed in by the host; the component's grid filters its rows by it.</summary>
    [ObservableProperty]
    public partial string? Filter { get; set; }

    /// <summary>
    /// Refreshes the "modified" (set vs default) flag for every top-level property by asking the engine
    /// whether each currently equals its default. Driven on selection (and after edits) so the indicator
    /// reflects engine truth without querying every entity in the world on each refresh.
    /// </summary>
    public async Task RefreshModifiedAsync(CancellationToken ct = default)
    {
        foreach (var (property, viewModel) in _resettable)
            await RefreshModifiedAsync(property, viewModel, ct).ContinueOnSameContext();
    }

    private async Task RefreshModifiedAsync(string property, PropertyViewModel viewModel, CancellationToken ct)
    {
        var result = await _engine
            .IsPropertyDefaultAsync(_entityId, Name, property, ct)
            .ContinueOnSameContext();
        if (result.Success)
            viewModel.IsModified = !result.Value;
    }

    private async void OnPropertyEdited(string property)
    {
        // The leaf mutated the live token inside Raw; read the property's current bare value back out.
        var bare = _raw[property] is JObject typed && typed.TryGetValue("value", out var value)
            ? value
            : _raw[property];
        if (bare is null)
            return;

        var result = await _engine
            .SetPropertyAsync(_entityId, Name, property, bare, CancellationToken.None)
            .ContinueOnSameContext();
        if (result.Success)
        {
            // An authored value is "set" by definition; mark it optimistically so the indicator appears at
            // once without a second round-trip per edit (which would double traffic during a scrub-drag).
            // The authoritative isDefault check still runs on the next selection / world refresh.
            if (_resettable.TryGetValue(property, out var viewModel))
                viewModel.IsModified = true;
            _onEdited();
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
        var result = await _engine
            .ResetPropertyAsync(_entityId, Name, property, CancellationToken.None)
            .ContinueOnSameContext();
        if (result.Success)
            _onEdited();
        else
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
