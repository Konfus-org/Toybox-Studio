using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// The grid itself: hands the shown target to the supplied <see cref="IPropertyNodeFactory"/> and hosts
/// the nodes it builds. The grid knows nothing about where values come from or what fills a row's
/// slots — that is entirely the factory's composition — it just renders the nodes and surfaces their
/// edits as one <see cref="Edited"/> signal for the host (dirty tracking, live preview, …).
/// </summary>
public sealed class PropertyGridViewModel : ObservableObject
{
    private readonly IPropertyNodeFactory _factory;

    public PropertyGridViewModel(IPropertyNodeFactory factory) => _factory = factory;

    /// <summary>Raised when any shown node (or descendant) is edited.</summary>
    public event Action? Edited;

    public ObservableCollection<PropertyNode> Nodes { get; } = [];

    /// <summary>Rebuilds the grid's rows for <paramref name="target"/>; null clears the grid.</summary>
    public void Show(object? target)
    {
        foreach (var node in Nodes)
            node.Edited -= NotifyEdited;
        Nodes.Clear();

        if (target is null)
            return;

        foreach (var node in _factory.CreateNodes(target))
        {
            node.Edited += NotifyEdited;
            Nodes.Add(node);
        }
    }

    private void NotifyEdited() => Edited?.Invoke();
}
