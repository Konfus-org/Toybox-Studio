using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Shared parent for every property widget view-model. Leaf widgets mutate the live JSON token in place and
/// raise <see cref="CommitChanges"/> so the host can persist the owning object (e.g. re-send a component
/// over RPC). The host leaves <see cref="CommitChanges"/> null for a read-only grid.
/// </summary>
public abstract class PropertyViewModel : ObservableObject
{
    // The live backing token, kept only so a search can match against the value text. Composite rows
    // (object/array) have no scalar value here — they match through their children instead.
    private readonly JToken? _value;

    protected PropertyViewModel(PropertyNode node)
    {
        Name = NameHumanizer.Humanize(node.Name);
        Type = node.Type;
        Category = node.Category;
        Description = node.Description;
        IsReadOnly = node.ReadOnly;
        Icon = node.Icon;
        IconColor = node.IconColor;
        _value = node.Value;
        ResetCommand = new RelayCommand(() => ResetToDefault?.Invoke());
    }

    public string Name { get; }

    public string Type { get; }

    /// <summary>
    /// Nesting level within the grid (0 at the top). Drives the row's indent, elbow connector, and the
    /// per-depth shading. Set by <see cref="PropertyViewModelFactory"/> as the tree is built.
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// Editor icon for the value's type ([[tbx::icon]]), or null. Badges composite headers.
    /// </summary>
    public string? Icon { get; }

    /// <summary>
    /// The icon's accent colour name (e.g. "BLUE"), or null.
    /// </summary>
    public string? IconColor { get; }

    /// <summary>
    /// True for rows that own a collapsible sub-tree (object/array). Leaf rows are false.
    /// </summary>
    public virtual bool HasChildren => false;

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

    /// <summary>
    /// Resets this property to its engine default. Set by the host (inspector) only on rows that support
    /// it — top-level, non-read-only component properties; null everywhere else (settings, nested rows).
    /// </summary>
    public Action? ResetToDefault { get; set; }

    /// <summary>True when a reset affordance should be offered for this row.</summary>
    public bool CanReset => ResetToDefault is not null;

    private bool _isModified;

    /// <summary>
    /// True when this property's current value differs from its engine default — i.e. it has actually been
    /// set/overridden. Drives the "modified" indicator and revert button. It is resolved asynchronously for
    /// top-level component properties (via <c>reflect.isDefault</c>) and left <c>false</c> wherever the
    /// default is unknown (nested fields, settings grids), so a row never shows a false "set" marker.
    /// </summary>
    public bool IsModified
    {
        get => _isModified;
        set
        {
            if (SetProperty(ref _isModified, value))
                OnPropertyChanged(nameof(State));
        }
    }

    /// <summary>
    /// The row's right-hand indicator state. Composite header rows show nothing; read-only rows a lock;
    /// otherwise a filled circle when the value differs from its default, a hollow circle when at default
    /// (or when the default is unknown — nested/settings rows — which read as default rather than "set").
    /// Rendered by <see cref="PropertyStateToIndicatorConverter"/>.
    /// </summary>
    public PropertyState State =>
        IsComposite ? PropertyState.None
        : IsReadOnly ? PropertyState.ReadOnly
        : IsModified ? PropertyState.NonDefault
        : PropertyState.Default;

    /// <summary>Invokes <see cref="ResetToDefault"/>; available for a reset affordance (e.g. context menu).</summary>
    public ICommand ResetCommand { get; }

    private bool _visible = true;

    /// <summary>
    /// Whether this row is shown under the active grid filter. The row's view binds its visibility here, so
    /// a non-matching row collapses out without rebuilding the tree. Driven by <see cref="ApplyFilter"/>.
    /// </summary>
    public bool Visible
    {
        get => _visible;
        private set => SetProperty(ref _visible, value);
    }

    /// <summary>The child rows to recurse into when filtering; empty for leaf rows, overridden by composites.</summary>
    protected virtual IEnumerable<PropertyViewModel> FilterChildren => [];

    /// <summary>
    /// Applies a header/value search across this row and its subtree, setting <see cref="Visible"/>
    /// throughout, and returns whether anything in the subtree ended up visible. An empty query shows
    /// everything; a row whose header or value matches shows itself and all its descendants; a non-matching
    /// row is shown only to keep a matching descendant in view (so context is preserved).
    /// </summary>
    public bool ApplyFilter(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ShowAll();
            return true;
        }

        var trimmed = query.Trim();
        if (Matches(trimmed))
        {
            ShowAll();
            return true;
        }

        var anyChildVisible = false;
        foreach (var child in FilterChildren)
            anyChildVisible |= child.ApplyFilter(trimmed);

        Visible = anyChildVisible;
        return anyChildVisible;
    }

    private void ShowAll()
    {
        Visible = true;
        foreach (var child in FilterChildren)
            child.ShowAll();
    }

    private bool Matches(string query) =>
        Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || ValueText.Contains(query, StringComparison.OrdinalIgnoreCase);

    // The bare value as text for value-search. A typed wrapper ({ type, value }) contributes only its value,
    // never the type token, so searching "int" doesn't match every integer.
    private string ValueText => _value switch
    {
        null => "",
        JObject obj when obj["value"] is { } inner => inner.ToString(Formatting.None),
        _ => _value.ToString(Formatting.None),
    };

    protected void RaiseCommit() => CommitChanges?.Invoke();
}
