using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;
using Toybox.Studio.Utils;
using Toybox.Studio.Utils.Extensions;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// List property — an expandable list of element sub-widgets. A resizable list (a C++ <c>std::vector</c>) gets a
/// "+" to append, a per-row drag handle to reorder, and a per-row "x" to delete; a fixed list renders the same
/// rows without those controls. It runs on either backing behind an <see cref="IArrayBacking"/>: the JSON path
/// (the accessor's value is a live <see cref="JArray"/>) mutates the token document in place — the pre-reflection
/// behaviour engine-authored data still depends on — while the typed path (the accessor's value is a real
/// <see cref="IList"/>, e.g. a <c>List&lt;Lod&gt;</c>) mutates the CLR list in place. Either way, a structural
/// edit re-commits the owning property through the accessor, the same round-trip a leaf edit uses.
/// </summary>
public sealed class ArrayPropertyViewModel : PropertyViewModel, IExpandable
{
    private readonly IArrayBacking _backing;
    private readonly int _depth;

    // True when entries can be added/removed/reordered — a std::vector (JSON: an element template was advertised;
    // typed: the accessor exposes an element type) on an editable grid.
    private readonly bool _resizable;

    // Maps each stable element key (a JSON token reference, or a typed element's object/index key) to the row VM
    // built for it, so a structural edit reuses the unchanged rows instead of recreating them all.
    private readonly Dictionary<object, PropertyViewModel> _rowsByKey = new(ReferenceEqualityComparer.Instance);

    // Nested items default collapsed; the user opens the ones they care about. The Summary ("N items") keeps a
    // collapsed list informative.
    private bool _isExpanded;

    private string _summary = "";

    public ArrayPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor, int depth = 0)
        : base(descriptor, accessor)
    {
        _depth = depth;
        // The backing value is a live JArray on the JSON path, and a real IList (not a JArray) on the typed path.
        _backing = accessor.Get() is IList and not JArray
            ? new TypedArrayBacking(descriptor, accessor)
            : new JsonArrayBacking(descriptor, accessor, RaiseCommit);
        _resizable = _backing.IsResizable;

        Items = [];
        AddCommand = new RelayCommand(Add, () => IsResizable);
        RemoveCommand = new RelayCommand<PropertyViewModel>(Remove);

        Disclosure = new DropdownPart(this);
        // The list's own row carries the append (+) affordance when it's resizable.
        if (IsResizable)
            Parts.Add(new ActionsPart(add: AddCommand));

        Rebuild();
    }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    /// <summary>
    /// True when entries can be added, reordered, and deleted — i.e. a <c>std::vector</c> on an editable grid. A
    /// fixed list or a read-only grid is false, so the view hides the +/handle/x affordances.
    /// </summary>
    public bool IsResizable => _resizable;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ObservableCollection<PropertyViewModel> Items { get; }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>Appends a fresh default element to the list.</summary>
    public ICommand AddCommand { get; }

    /// <summary>Removes the given element row from the list.</summary>
    public ICommand RemoveCommand { get; }

    protected override IEnumerable<PropertyViewModel> FilterChildren => Items;

    /// <summary>Restores the list to a default (count + element values) — the settings reset path (JSON only).</summary>
    public override void ApplyValue(JToken token)
    {
        if (!_backing.ResetTo(token))
            return;
        Rebuild();
        RaiseCommit();
    }

    /// <summary>
    /// Reorders the entry at <paramref name="from"/> to <paramref name="to"/> (called by the drag-handle
    /// behavior). Moves the backing element so the committed list matches the on-screen order.
    /// </summary>
    public void Move(int from, int to)
    {
        if (!IsResizable)
            return;

        // from/to are VISIBLE row positions; map them to backing element keys (a [[hidden]] element makes visible
        // and backing indices diverge) so the live list moves the same element the user dragged.
        var visible = _backing.VisibleElements().Select(element => element.Key).ToList();
        if (from == to || from < 0 || to < 0 || from >= visible.Count || to >= visible.Count)
            return;

        _backing.Move(visible[from], visible[to], to > from);
        Rebuild();
        RaiseCommit();
    }

    /// <summary>
    /// The widget type token of this list's elements — the variant key a copied element is tagged with, so the
    /// "Paste Item" affordance only offers an element of the matching type. Read from a live row when there is
    /// one; otherwise inferred from the element shape so an empty list can still accept a paste.
    /// </summary>
    public string ElementType => Items.Count > 0 ? Items[0].Type : _backing.ElementType;

    /// <summary>True when <paramref name="item"/> is a list element that can move toward the start.</summary>
    public bool CanMoveUp(PropertyViewModel item) => Items.IndexOf(item) > 0;

    /// <summary>True when <paramref name="item"/> is a list element that can move toward the end.</summary>
    public bool CanMoveDown(PropertyViewModel item)
    {
        var index = Items.IndexOf(item);
        return index >= 0 && index < Items.Count - 1;
    }

    /// <summary>Moves <paramref name="item"/> one place toward the start of the list.</summary>
    public void MoveUp(PropertyViewModel item)
    {
        var index = Items.IndexOf(item);
        if (index > 0)
            Move(index, index - 1);
    }

    /// <summary>Moves <paramref name="item"/> one place toward the end of the list.</summary>
    public void MoveDown(PropertyViewModel item)
    {
        var index = Items.IndexOf(item);
        if (index >= 0 && index < Items.Count - 1)
            Move(index, index + 1);
    }

    /// <summary>Inserts a copy of <paramref name="item"/>'s backing element right after it.</summary>
    public void Duplicate(PropertyViewModel item)
    {
        if (!IsResizable || KeyFor(item) is not { } key)
            return;

        _backing.Duplicate(key);
        Rebuild();
        RaiseCommit();
    }

    /// <summary>Removes the given element row from the list (the context-menu / button delete).</summary>
    public void RemoveItem(PropertyViewModel item) => Remove(item);

    /// <summary>Appends a copy of a bare element value (a pasted item) to the end of the list.</summary>
    public void AppendValue(JToken value)
    {
        if (!IsResizable)
            return;

        _backing.AppendValue(value);
        Rebuild();
        RaiseCommit();
    }

    // A list is "set" when any element is (value-wise); recompute when an element's modified flag moves.
    private void OnElementChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(IsModified) or nameof(State))
            RecomputeModified();
    }

    private void RecomputeModified() => IsModified = Items.Any(item => item.IsModified);

    private void Add()
    {
        if (!IsResizable)
            return;

        _backing.Add();
        Rebuild();
        RaiseCommit();
    }

    private void Remove(PropertyViewModel? item)
    {
        if (!IsResizable || item is null || KeyFor(item) is not { } key)
            return;

        _backing.Remove(key);
        Rebuild();
        RaiseCommit();
    }

    // The stable backing key a visible row was built from (its entry in _rowsByKey), or null if the row is no
    // longer mapped. Used so structural edits target the correct element regardless of hidden elements.
    private object? KeyFor(PropertyViewModel item)
    {
        foreach (var (key, row) in _rowsByKey)
            if (ReferenceEquals(row, item))
                return key;
        return null;
    }

    // Rebuilds the child view-models from the (possibly mutated) backing, reusing each unchanged row in place
    // rather than tearing the whole list down. A row is keyed by its element's stable backing key (a JSON token
    // reference, or a typed element's object/index key), so an append/delete/reorder reuses every other row: the
    // grid keeps their expansion state and doesn't re-flash the entrance animation.
    private void Rebuild()
    {
        var desired = new List<PropertyViewModel>();
        var rebuilt = new Dictionary<object, PropertyViewModel>(ReferenceEqualityComparer.Instance);

        foreach (var (key, descriptor, accessor) in _backing.VisibleElements())
        {
            // Reuse the row built for this key when it's still in shape (Sync refreshes its value in place);
            // otherwise build a fresh one and wire its parts.
            if (!_rowsByKey.TryGetValue(key, out var element) || !element.Sync(descriptor, accessor))
            {
                element = PropertyViewModelFactory.Create(descriptor, accessor, _depth + 1);
                element.PropertyChanged += OnElementChanged;
                // A resizable list's elements carry their own reorder grip + delete affordance, and point back at
                // this list so the property context menu can offer the item actions.
                if (IsResizable)
                {
                    element.OwningList = this;
                    element.Parts.Add(new HandlePart(this, element));
                    element.Parts.Add(new ActionsPart(remove: new RelayCommand(() => Remove(element))));
                }
            }

            rebuilt[key] = element;
            desired.Add(element);
        }

        // Detach any row that didn't survive (its element left the list, or it was replaced in place).
        foreach (var (_, row) in _rowsByKey)
            if (!desired.Contains(row))
                row.PropertyChanged -= OnElementChanged;

        _rowsByKey.Clear();
        foreach (var (key, row) in rebuilt)
            _rowsByKey[key] = row;

        // Reconcile the visible collection in place so only the changed rows move (keeps the rest mounted).
        Items.Reconcile(desired);

        Summary = $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";
        RecomputeModified();
    }
}
