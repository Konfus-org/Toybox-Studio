using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.EngineApi;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// List property — an expandable list of element sub-widgets. A resizable list (a C++ <c>std::vector</c>,
/// which the engine marks by advertising an element template) gets a "+" to append, a per-row drag handle to
/// reorder, and a per-row "x" to delete; a fixed list (no template) renders the same rows without those
/// controls. Every structural edit mutates the live backing <see cref="JArray"/> in place and re-commits the
/// owning component property — the same round-trip a leaf edit uses, so nested lists work too.
/// </summary>
public sealed class ArrayPropertyViewModel : PropertyViewModel
{
    private readonly JArray? _array;
    private readonly JToken? _elementTemplate;
    private readonly Action? _commit;
    private readonly int _depth;

    public ArrayPropertyViewModel(PropertyNode node, Action? commit, int depth = 0) : base(node)
    {
        _array = node.Value as JArray;
        _elementTemplate = node.ElementTemplate;
        _commit = commit;
        _depth = depth;

        Items = [];
        AddCommand = new RelayCommand(Add, () => IsResizable);
        RemoveCommand = new RelayCommand<PropertyViewModel>(Remove);

        Rebuild();
    }

    public override bool IsComposite => true;

    public override bool HasChildren => true;

    /// <summary>
    /// True when entries can be added, reordered, and deleted — i.e. a <c>std::vector</c> (the engine
    /// advertised an element template) on an editable grid. A fixed list or a read-only grid is false, so
    /// the view hides the +/handle/x affordances.
    /// </summary>
    public bool IsResizable => _array is not null && _elementTemplate is not null && _commit is not null;

    // Nested items default collapsed (components and category groups default expanded); the user opens the
    // ones they care about. The Summary ("N items") keeps a collapsed list informative.
    private bool _isExpanded;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ObservableCollection<PropertyViewModel> Items { get; }

    private string _summary = "";

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <summary>Appends a fresh default element (a clone of the engine's template) to the list.</summary>
    public ICommand AddCommand { get; }

    /// <summary>Removes the given element row from the list.</summary>
    public ICommand RemoveCommand { get; }

    protected override IEnumerable<PropertyViewModel> FilterChildren => Items;

    private void Add()
    {
        if (_array is null || _elementTemplate is null || _commit is null)
            return;

        _array.Add(_elementTemplate.DeepClone());
        Rebuild();
        RaiseCommit();
    }

    private void Remove(PropertyViewModel? item)
    {
        if (_array is null || _commit is null || item is null)
            return;

        var index = Items.IndexOf(item);
        if (index < 0 || index >= _array.Count)
            return;

        _array[index].Remove();
        Rebuild();
        RaiseCommit();
    }

    /// <summary>
    /// Reorders the entry at <paramref name="from"/> to <paramref name="to"/> (called by the drag-handle
    /// behavior). Moves the live JSON token so the committed array matches the on-screen order.
    /// </summary>
    public void Move(int from, int to)
    {
        if (_array is null || _commit is null)
            return;

        if (from == to || from < 0 || to < 0 || from >= _array.Count || to >= _array.Count)
            return;

        var token = _array[from];
        token.Remove();
        _array.Insert(to, token);
        Rebuild();
        RaiseCommit();
    }

    // Rebuilds the child view-models from the (possibly mutated) backing array. Re-parsing — rather than
    // shuffling the existing VMs — keeps each row bound to its real array slot after an add/remove/reorder
    // detaches and re-homes the JSON tokens, and renumbers the [i] labels for free.
    private void Rebuild()
    {
        Items.Clear();
        if (_array is not null)
        {
            foreach (var child in JsonParser.ParseArrayElements(_array))
                Items.Add(PropertyViewModelFactory.Create(child, _commit, _depth + 1));
        }

        Summary = $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";
    }
}
