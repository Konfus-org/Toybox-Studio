using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A run of properties under one [[tbx::category]] heading. The default (null) group renders header-less
/// at the top; named groups render under a collapsible header.
/// </summary>
public sealed class PropertyCategoryGroup : ObservableObject
{
    public PropertyCategoryGroup(string? name, IEnumerable<PropertyViewModel> items)
    {
        Name = name;
        Items = new ObservableCollection<PropertyViewModel>(items);
    }

    public string? Name { get; }

    public bool HasHeader => !string.IsNullOrEmpty(Name);

    public ObservableCollection<PropertyViewModel> Items { get; }

    private bool _visible = true;

    /// <summary>Whether the group (header + rows) is shown — false once a filter hides all its rows.</summary>
    public bool Visible
    {
        get => _visible;
        private set => SetProperty(ref _visible, value);
    }

    /// <summary>Recomputes group visibility from its rows' current filter state.</summary>
    public void RefreshVisibility() => Visible = Items.Any(item => item.Visible);
}
