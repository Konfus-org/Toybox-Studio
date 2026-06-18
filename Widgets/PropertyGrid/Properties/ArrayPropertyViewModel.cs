using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// List property — an expandable list of element sub-widgets. A resizable list (a C++ <c>std::vector</c>,
/// which the engine marks by advertising an element template) gets a "+" to append, a per-row drag handle to
/// reorder, and a per-row "x" to delete; a fixed list (no template) renders the same rows without those
/// controls. Every structural edit mutates the live backing <see cref="JArray"/> in place and re-commits the
/// owning component property — the same round-trip a leaf edit uses, so nested lists work too.
/// </summary>
public sealed class ArrayPropertyViewModel : PropertyViewModel, IExpandable
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

        Dropdown = new DropdownPart(this);
        // The list's own row carries the append (+) affordance when it's resizable.
        if (IsResizable)
            Actions = new ActionsPart(add: AddCommand);

        Rebuild();
    }

    public override bool IsComposite => true;

    // A list is "set" when any element is (value-wise); recompute when an element's modified flag moves.
    private void OnElementChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(IsModified) or nameof(State))
            RecomputeModified();
    }

    private void RecomputeModified() => IsModified = Items.Any(item => item.IsModified);

    /// <summary>The backing array token, so the whole list can be compared/reset as a unit.</summary>
    public override JToken? CurrentValue => _array;

    /// <summary>Restores the list to a default array (count + element values) — the settings reset path.</summary>
    public override void ApplyValue(JToken token)
    {
        if (_array is null || token is not JArray defaults)
            return;

        _array.Clear();
        foreach (var element in defaults)
            _array.Add(element.DeepClone());
        Rebuild();
        RaiseCommit();
    }

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
            {
                var element = PropertyViewModelFactory.Create(Headered(child), _commit, _depth + 1);
                element.PropertyChanged += OnElementChanged;
                // A resizable list's elements carry their own reorder grip + delete affordance, rendered in
                // the element row's Handle / Actions slots.
                if (IsResizable)
                {
                    element.Handle = new HandlePart(this, element);
                    element.Actions = new ActionsPart(remove: new RelayCommand(() => Remove(element)));
                }

                Items.Add(element);
            }
        }

        Summary = $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";
        RecomputeModified();
    }

    // A struct/object element shows a meaningful header instead of its "[i]" index when it carries an obvious
    // identity: a child literally named "name" (a string), or failing that the first handle child resolved to
    // its asset name. Anything else keeps the index. Returns the node unchanged when nothing better is found.
    private static PropertyNode Headered(PropertyNode element)
    {
        if (element.Children.Count == 0)
            return element;

        var label = DeriveHeader(element);
        return label is null ? element : Relabel(element, label);
    }

    private static string? DeriveHeader(PropertyNode element)
    {
        var nameChild = element.Children.FirstOrDefault(
            child => string.Equals(child.Name, "name", StringComparison.OrdinalIgnoreCase));
        if (nameChild?.Value is { Type: JTokenType.String } nameValue)
        {
            var text = nameValue.Value<string>();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        var handleChild = element.Children.FirstOrDefault(child => child.Type == "handle");
        if (handleChild?.Value is { } handleValue && PropertyViewRegistry.Assets is { } assets)
        {
            var resolved = assets.ResolveName(handleValue.Value<long>());
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        return null;
    }

    // PropertyNode is init-only, so override the header by cloning it with a Label (which the view-model
    // prefers over the humanized "[i]" name).
    private static PropertyNode Relabel(PropertyNode node, string label) => new()
    {
        Name = node.Name,
        Type = node.Type,
        Value = node.Value,
        Choices = node.Choices,
        Category = node.Category,
        Description = node.Description,
        ReadOnly = node.ReadOnly,
        Hidden = node.Hidden,
        Order = node.Order,
        View = node.View,
        Label = label,
        Icon = node.Icon,
        IconColor = node.IconColor,
        IsDefault = node.IsDefault,
        ElementTemplate = node.ElementTemplate,
        Children = node.Children,
    };
}
