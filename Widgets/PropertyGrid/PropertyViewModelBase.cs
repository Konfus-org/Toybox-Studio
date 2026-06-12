using CommunityToolkit.Mvvm.ComponentModel;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Base for every property widget view-model. Leaf widgets mutate the live JSON token in place and then
/// raise <see cref="CommitChanges"/> so the host can persist the owning object (e.g. re-send a component
/// over RPC). The host leaves <see cref="CommitChanges"/> null for a read-only grid.
/// </summary>
public abstract class PropertyViewModelBase : ObservableObject
{
    protected PropertyViewModelBase(PropertyNode node)
    {
        Name = node.Name;
        Type = node.Type;
        Category = node.Category;
        Description = node.Description;
        IsReadOnly = node.ReadOnly;
    }

    public string Name { get; }

    public string Type { get; }

    /// <summary>
    /// Group heading ([[tbx::category]]), or null for the default (header-less) group.
    /// </summary>
    public string? Category { get; }

    /// <summary>
    /// Tooltip text ([[editor::description]]), or null.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// True for [[editor::readonly]] fields: editable leaf views disable their control, and the host
    /// also withholds the commit action (see <see cref="PropertyViewModelFactory"/>).
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// True for composite rows (object/array) that render full-width rather than name+value.
    /// </summary>
    public virtual bool IsComposite => false;

    /// <summary>
    /// Raised after a leaf edits its backing token. Null = read-only.
    /// </summary>
    public Action? CommitChanges { get; set; }

    protected void RaiseCommit() => CommitChanges?.Invoke();
}
