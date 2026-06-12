using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A reusable, type-driven property grid. Bind <see cref="Items"/> to a collection of
/// <see cref="PropertyViewModelBase"/>; rows render through the widget DataTemplate matching their type,
/// share one auto-sized (resizable) name column, and group under their [[tbx::category]] headings.
/// </summary>
public partial class PropertyGridView : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty =
        AvaloniaProperty.Register<PropertyGridView, IEnumerable?>(nameof(Items));

    public static readonly DirectProperty<PropertyGridView, bool> IsEmptyProperty =
        AvaloniaProperty.RegisterDirect<PropertyGridView, bool>(nameof(IsEmpty), o => o.IsEmpty);

    private INotifyCollectionChanged? _observed;
    private bool _isEmpty = true;

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
    /// True when there are no rows to show (drives the empty-state ghost).
    /// </summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetAndRaise(IsEmptyProperty, ref _isEmpty, value);
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
        var byCategory = new Dictionary<string, List<PropertyViewModelBase>>();
        const string none = "\0none";

        foreach (var item in Items.OfType<PropertyViewModelBase>())
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
            Groups.Add(new PropertyCategoryGroup(category, list));
        }

        IsEmpty = Groups.Count == 0;
    }
}
