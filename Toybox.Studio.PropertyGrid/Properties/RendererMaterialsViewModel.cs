using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Project;
using Toybox.Studio.Utils;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The Renderer component's material editor. Rather than a bare numbered list, it shows one labeled row per the
/// bound model's hard material slots — the slot names are extracted from the source file at import and fetched
/// live via <c>editor.modelSlots</c>. Each row is an asset picker that overrides that slot's material; an empty
/// pick inherits the model's own slot. Picking a different model re-fetches and relabels the slots. As a
/// composite row it renders a collapsible "Materials" header above its slot rows.
/// </summary>
public sealed partial class RendererMaterialsViewModel : PropertyViewModel, IExpandable
{
    // The live backing material-override array (a vector<Handle>): aligned 1:1 with the model's slots, padded
    // with empty (inherit) handles so every slot has an attached token its picker can replace in place. On the JSON
    // path this IS the accessor's live token (mutations land straight in the describe body); on the typed path it's
    // a working copy the commit writes back into the reflected list (see _typed / Commit).
    private readonly JArray _materials;
    private readonly AssetCatalog _catalog;

    // The materials field's asset-type filter (its [[asset("mti","mat")]] choices), threaded onto each slot's
    // picker so the chooser only lists materials.
    private readonly IReadOnlyList<string>? _choices;
    private readonly Action? _commit;

    // True when the field is sourced from the typed reflected model (a List<AssetHandle>) rather than a live JSON
    // array: the working _materials is a detached view, so each commit must read it back into the reflected list.
    private readonly bool _typed;

    // The section defaults open: the slot rows are the point of the editor, not an aside to drill into.
    private bool _isExpanded = true;

    public RendererMaterialsViewModel(
        PropertyDescriptor descriptor, IValueAccessor accessor, AssetCatalog catalog, ulong modelId,
        Action? commit, int depth)
        : base(descriptor, accessor)
    {
        // The JSON path hands over the live vector<Handle> token (edited in place); the typed path yields a
        // List<AssetHandle>, so build a working JArray from its bare form and write it back on each commit.
        _typed = accessor.Get() is not JArray;
        _materials = _typed ? (EngineSyncValue.WriteBare(accessor.Get()) as JArray ?? []) : accessor.Get() as JArray ?? [];
        _catalog = catalog;
        _choices = descriptor.Choices;
        _commit = commit;
        Depth = depth;

        Slots = [];
        Disclosure = new DropdownPart(this);
        // Resetting the section reverts every slot to the model's default (an empty/inherit handle).
        ResetToDefault = ResetAll;
        ReloadAsync(modelId).FireAndForget();
    }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    /// <summary>The per-slot material pickers, one per the model's material slots, labeled by slot name.</summary>
    public ObservableCollection<PropertyViewModel> Slots { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>True when the bound model resolves to no material slots (or none is assigned), so the view shows
    /// a hint instead of an empty section.</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    protected override IEnumerable<PropertyViewModel> FilterChildren => Slots;

    /// <summary>
    /// Re-fetches the model's slots and rebuilds the per-slot picker rows. Called on construction and whenever
    /// the bound model changes. A model with no slots (or none assigned) clears the rows and shows the hint.
    /// </summary>
    public async Task ReloadAsync(ulong modelId)
    {
        foreach (var slot in Slots)
            slot.PropertyChanged -= OnSlotChanged;
        Slots.Clear();

        var slots = await _catalog.ModelSlotsAsync(modelId).ContinueOnSameContext();

        // Size the rows to the model's slots, but never hide existing overrides: if the stored override list is
        // somehow longer than the model's slot list, keep those trailing entries as index-labeled rows so no
        // authored material is silently dropped.
        var count = Math.Max(slots.Count, _materials.Count);
        for (var index = 0; index < count; index++)
        {
            // Each row needs a live, attached token so its picker can replace it in place; pad the backing array
            // with empty (inherit) handles for any slot the stored list doesn't reach.
            if (index >= _materials.Count)
                _materials.Add(new JValue(0UL));

            var label = index < slots.Count ? slots[index].Name : $"Slot {index}";
            Slots.Add(BuildSlot(index, label));
        }

        IsEmpty = Slots.Count == 0;
        Recompute();
    }

    // The editor owns its slot rows (rebuilt on a model change or on reselect) rather than zipping them against
    // the describe payload — and the backing array is padded to the model's slot count, so a structural compare
    // against the lean engine array would always miss. Report "in sync" so live-value tracking keeps the rows in
    // place instead of forcing a full inspector rebuild every tick.
    protected override bool SyncCore(IValueAccessor accessor) => true;

    private PropertyViewModel BuildSlot(int index, string label)
    {
        var token = _materials[index];
        var descriptor = new PropertyDescriptor
        {
            Name = $"materials[{index}]",
            Type = EngineTypes.Handle,
            Choices = _choices,
            Label = label,
            // An empty handle inherits the model's slot (its default); an assigned material is an override.
            IsDefault = ReadId(token) == 0,
        };

        // The slot's picker reads/writes the live handle token in place; its own commit is null so the row's
        // CommitChanges override (below) re-commits the whole materials array instead.
        var slotAccessor = new JsonAccessor(token, EngineTypes.Handle, null);
        var picker = new HandlePickerPropertyViewModel(descriptor, slotAccessor, _catalog) { Depth = Depth + 1 };
        picker.IsModified = ReadId(token) != 0;
        // Keep the row's indicator in step with its value, and re-commit the whole materials array on each pick.
        picker.CommitChanges = () =>
        {
            picker.IsModified = picker.CurrentId != 0;
            Commit();
        };
        // Resetting a slot reverts it to the model's own default — an empty/inherit handle.
        picker.ResetToDefault = () => picker.ApplyValue(new JValue(0UL));
        picker.PropertyChanged += OnSlotChanged;
        return picker;
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(IsModified) or nameof(State))
            Recompute();
    }

    // Persists the whole materials array after a slot pick / reset. On the JSON path the slot pickers already
    // mutated the live token, so this just fires the describe reflect.set commit; on the typed path _materials is a
    // detached working copy, so read it back into the reflected List<AssetHandle> and push through the accessor.
    private void Commit()
    {
        if (_typed)
        {
            Accessor.Set(EngineSyncValue.ReadBare(Accessor.ValueType, _materials));
            Accessor.Commit();
            return;
        }

        _commit?.Invoke();
    }

    // The section reads as "set" exactly when any slot carries an override.
    private void Recompute() => IsModified = Slots.Any(slot => slot.IsModified);

    private void ResetAll()
    {
        foreach (var slot in Slots)
            slot.ResetToDefault?.Invoke();
    }

    private static ulong ReadId(JToken? token) => token?.Value<ulong>() ?? 0;
}
