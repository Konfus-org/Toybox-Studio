using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// One section of a material instance's override editor — its Textures or its Parameters — presented as a
/// nested STRUCT row rather than an accent category band: a collapsible header with the shared depth/indent,
/// hover and nod, plus a state/reset indicator. It reads as "set" when any slot is overridden, and resetting
/// it reverts every slot in the section to the base material value (each slot's own reset). Backed by the
/// shared <see cref="PropertyViewModel"/> so it flows through the normal <c>PropertyRow</c> chrome and the
/// <see cref="StateIndicatorPart"/> / <see cref="DropdownPart"/> slots exactly like a real struct.
/// </summary>
public sealed class MaterialSectionViewModel : PropertyViewModel, IExpandable
{
    private bool _isExpanded = true;

    public MaterialSectionViewModel(
        string name, ObservableCollection<MaterialSlotViewModel> slots, int depth)
        : base(new PropertyNode { Name = name, Type = "struct", IsDefault = true })
    {
        Slots = slots;
        Depth = depth;
        Disclosure = new DropdownPart(this);
        // A struct-style reset: revert every slot in the section to its base value (a no-op for slots that
        // aren't overridden). Always offered, like a real struct row; the indicator shows whether it's set.
        ResetToDefault = ResetAll;

        foreach (var slot in Slots)
            Track(slot);
        Slots.CollectionChanged += OnSlotsChanged;
        Recompute();
    }

    public ObservableCollection<MaterialSlotViewModel> Slots { get; }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    /// <summary>True once at least one slot exists, so the section is shown.</summary>
    public bool HasSlots => Slots.Count > 0;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    protected override IEnumerable<PropertyViewModel> FilterChildren => Slots.Select(slot => slot.Editor);

    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null)
            foreach (MaterialSlotViewModel slot in args.NewItems)
                Track(slot);

        OnPropertyChanged(nameof(HasSlots));
        Recompute();
    }

    // A slot's Editor is swapped out on reset, so follow the slot AND its current editor.
    private void Track(MaterialSlotViewModel slot)
    {
        slot.PropertyChanged += OnSlotChanged;
        slot.Editor.PropertyChanged += OnEditorChanged;
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MaterialSlotViewModel.Editor) && sender is MaterialSlotViewModel slot)
        {
            slot.Editor.PropertyChanged -= OnEditorChanged;
            slot.Editor.PropertyChanged += OnEditorChanged;
            Recompute();
        }
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(IsModified) or nameof(State))
            Recompute();
    }

    // The section is "set" exactly when one of its slots carries an override.
    private void Recompute() => IsModified = Slots.Any(slot => slot.Editor.IsModified);

    private void ResetAll()
    {
        foreach (var slot in Slots)
            slot.Editor.ResetToDefault?.Invoke();
    }
}
