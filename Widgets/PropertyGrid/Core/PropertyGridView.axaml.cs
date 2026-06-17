using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A reusable, type-driven property grid. Bind <see cref="Items"/> to a collection of
/// <see cref="PropertyViewModel"/>; rows render through the widget DataTemplate matching their type,
/// share one auto-sized (resizable) name column, and group under their [[tbx::category]] headings.
/// </summary>
public partial class PropertyGridView : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty =
        AvaloniaProperty.Register<PropertyGridView, IEnumerable?>(nameof(Items));

    public static readonly StyledProperty<string?> FilterProperty =
        AvaloniaProperty.Register<PropertyGridView, string?>(nameof(Filter));

    public static readonly StyledProperty<bool> EmbeddedProperty =
        AvaloniaProperty.Register<PropertyGridView, bool>(nameof(Embedded));

    public static readonly DirectProperty<PropertyGridView, bool> IsEmptyProperty =
        AvaloniaProperty.RegisterDirect<PropertyGridView, bool>(nameof(IsEmpty), o => o.IsEmpty);

    public static readonly DirectProperty<PropertyGridView, bool> HasVisibleItemsProperty =
        AvaloniaProperty.RegisterDirect<PropertyGridView, bool>(
            nameof(HasVisibleItems), o => o.HasVisibleItems);

    private INotifyCollectionChanged? _observed;
    private bool _isEmpty = true;
    private bool _hasVisibleItems = true;

    public PropertyGridView()
    {
        InitializeComponent();
    }

    public IEnumerable? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>
    /// A header/value search applied to the rows: a row stays visible when its name or value matches, or to
    /// keep a matching descendant in view. Empty/null shows everything. Owned by the host panel's search box.
    /// </summary>
    public string? Filter
    {
        get => GetValue(FilterProperty);
        set => SetValue(FilterProperty, value);
    }

    /// <summary>
    /// When true the grid drops its own clay card chrome (background, shadow, rounding, outer margin) and
    /// renders its rows flat, so a host can wrap the grid in its own card — e.g. the inspector, where the
    /// component header and the grid combine into a single component card. Standalone grids (settings) leave
    /// this false and card their groups themselves.
    /// </summary>
    public bool Embedded
    {
        get => GetValue(EmbeddedProperty);
        set => SetValue(EmbeddedProperty, value);
    }

    /// <summary>
    /// True when there are no rows to show (drives the empty-state ghost).
    /// </summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetAndRaise(IsEmptyProperty, ref _isEmpty, value);
    }

    /// <summary>
    /// True when at least one row passes the current <see cref="Filter"/>. Lets a host hide a whole grid
    /// (e.g. an inspector component card) when nothing in it matches the search.
    /// </summary>
    public bool HasVisibleItems
    {
        get => _hasVisibleItems;
        private set => SetAndRaise(HasVisibleItemsProperty, ref _hasVisibleItems, value);
    }

    /// <summary>
    /// The category groups rendered by the view, derived from <see cref="Items"/>.
    /// </summary>
    public ObservableCollection<PropertyCategoryGroup> Groups { get; } = [];

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsProperty)
        {
            Rewire();
            RebuildGroups();
        }
        else if (change.Property == FilterProperty)
        {
            ApplyFilter();
        }
    }

    private void Rewire()
    {
        if (_observed is not null)
            _observed.CollectionChanged -= OnItemsChanged;

        _observed = Items as INotifyCollectionChanged;
        if (_observed is not null)
            _observed.CollectionChanged += OnItemsChanged;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildGroups();

    private void RebuildGroups()
    {
        Groups.Clear();
        if (Items is null)
            return;

        // Preserve first-appearance order of categories; the uncategorized group floats to the top.
        var order = new List<string?>();
        var byCategory = new Dictionary<string, List<PropertyViewModel>>();
        const string none = "\0none";

        foreach (var item in Items.OfType<PropertyViewModel>())
        {
            var key = string.IsNullOrEmpty(item.Category) ? none : item.Category!;
            if (!byCategory.TryGetValue(key, out var list))
            {
                list = [];
                byCategory[key] = list;
                order.Add(key == none ? null : key);
            }

            list.Add(item);
        }

        foreach (var category in order.OrderBy(c => c is null ? 0 : 1))
        {
            var list = byCategory[category ?? none];
            // Leaf properties (no expandable children) float above the collapsible struct/array sections, so
            // the simple fields read together at the top and the groups settle to the bottom. OrderBy is
            // stable, so declaration order is preserved within each partition.
            var ordered = list.OrderBy(item => item.HasChildren ? 1 : 0).ToList();
            Groups.Add(new PropertyCategoryGroup(category, ordered));
        }

        IsEmpty = Groups.Count == 0;

        // Re-apply the active search to the freshly built rows so filtering survives an Items change.
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var anyVisible = false;
        foreach (var item in (Items ?? Enumerable.Empty<object>()).OfType<PropertyViewModel>())
            anyVisible |= item.ApplyFilter(Filter);

        foreach (var group in Groups)
            group.RefreshVisibility();

        HasVisibleItems = anyVisible;
    }
}
