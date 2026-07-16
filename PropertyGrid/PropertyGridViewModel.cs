using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The grid itself: hands the shown target to the supplied <see cref="IPropertyNodeFactory"/> and hosts
/// the nodes it builds. The grid knows nothing about where values come from or what fills a row's
/// slots — that is entirely the factory's composition — it just renders the nodes and surfaces their
/// edits as one <see cref="Edited"/> signal for the host (dirty tracking, live preview, …). The nodes are
/// also gathered into <see cref="Groups"/> by their <see cref="PropertyNode.Category"/> so the view can render
/// one card per category (the uncategorized nodes forming a single header-less card above the rest).
/// </summary>
public sealed class PropertyGridViewModel : ObservableObject
{
    private readonly IPropertyNodeFactory _factory;

    public PropertyGridViewModel(IPropertyNodeFactory factory) => _factory = factory;

    /// <summary>Raised when any shown node (or descendant) is edited.</summary>
    public event Action? Edited;

    public ObservableCollection<PropertyNode> Nodes { get; } = [];

    /// <summary>The nodes grouped into cards by category — the uncategorized group first (header-less), then
    /// the named categories in first-appearance order. The view binds this; <see cref="Nodes"/> stays the flat
    /// source of truth.</summary>
    public ObservableCollection<PropertyCategoryGroup> Groups { get; } = [];

    /// <summary>Whether the grid currently has any rows. False for a target with no editable properties
    /// (an identity-only asset, whose members are all hidden), so a host can hide an empty inspector.</summary>
    public bool HasNodes => Nodes.Count > 0;

    /// <summary>Rebuilds the grid's rows for <paramref name="target"/>; null clears the grid.</summary>
    public void Show(object? target)
    {
        foreach (var node in Nodes)
            node.Edited -= NotifyEdited;
        Nodes.Clear();
        Groups.Clear();

        if (target is not null)
        {
            foreach (var node in _factory.CreateNodes(target))
            {
                node.Edited += NotifyEdited;
                Nodes.Add(node);
            }

            RebuildGroups();
        }

        OnPropertyChanged(nameof(HasNodes));
    }

    // The uncategorized nodes float to the top as one header-less card; the named categories follow in the
    // order they first appear. GroupBy preserves both the key order and each node's order within its group, so
    // the factory's leaves-before-composites sort still holds inside every card.
    private void RebuildGroups()
    {
        var uncategorized = Nodes.Where(node => string.IsNullOrEmpty(node.Category)).ToList();
        if (uncategorized.Count > 0)
            Groups.Add(new PropertyCategoryGroup(null, uncategorized));

        foreach (var group in Nodes.Where(node => !string.IsNullOrEmpty(node.Category)).GroupBy(node => node.Category!))
            Groups.Add(new PropertyCategoryGroup(group.Key, group.ToList()));
    }

    private void NotifyEdited() => Edited?.Invoke();
}
