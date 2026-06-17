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
        Name = string.IsNullOrEmpty(node.Label) ? NameHumanizer.Humanize(node.Name) : node.Label;
        RawName = node.Name;
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

    /// <summary>
    /// The property's raw key (un-humanized), used to match a leaf against a parallel document — e.g. the
    /// settings grid pairing each row with its default value to drive the reset affordance.
    /// </summary>
    public string RawName { get; }

    public string Type { get; }

    /// <summary>
    /// Nesting level within the grid (0 at the top). Drives the row's indent and per-depth colour shading
    /// (depth is read from colour, not a connector). Set by <see cref="PropertyViewModelFactory"/> as the
    /// tree is built.
    /// </summary>
    public int Depth { get; set; }

    /// <summary>
    /// True for anything below the grid's top level. A nested struct/array section drops the accent "section"
    /// look (which is reserved for categories and root-level structs) and instead reads like a plain property
    /// header, so it aligns with the sibling rows rather than stacking a second coloured band.
    /// </summary>
    public bool IsNested => Depth > 0;

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

    // Set while a fresh snapshot is being pushed in via <see cref="Sync"/>: tracking engine truth must move
    // the displayed value WITHOUT persisting it straight back as if the user had just typed it.
    private bool _suppressCommit;

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

    protected void RaiseCommit()
    {
        if (!_suppressCommit)
            CommitChanges?.Invoke();
    }

    /// <summary>
    /// This leaf's current value as a bare JSON token, or null for composites/widgets that don't expose a
    /// single scalar. Used to compare against a known default (the settings grid) without a per-type cast.
    /// </summary>
    public virtual JToken? CurrentValue => null;

    /// <summary>
    /// Sets this leaf's value from a bare JSON token, going through the same path as a user edit (so it
    /// persists and the bound control refreshes). No-op for composites/widgets without a scalar value.
    /// Used by the reset affordance to restore a known default.
    /// </summary>
    public virtual void ApplyValue(JToken token) { }

    /// <summary>
    /// Refreshes this row's displayed value(s) from a fresh snapshot of the same property, WITHOUT persisting
    /// the change back to the engine. Used to track a running game's live values in place, so the grid's
    /// controls (and any in-progress edit) are kept rather than torn down and rebuilt every tick. Returns
    /// false when the new node's shape no longer matches this row (e.g. an array changed length, or a value
    /// of a type this row can't update in place actually changed), telling the host to rebuild instead.
    /// </summary>
    public bool Sync(PropertyNode node)
    {
        _suppressCommit = true;
        try
        {
            return SyncCore(node);
        }
        finally
        {
            _suppressCommit = false;
        }
    }

    /// <summary>
    /// Type-specific in-place value refresh for <see cref="Sync"/>. The default handles every row that does
    /// not override it conservatively: it reports success only when the value is unchanged from what this row
    /// was built with, so an untracked type whose value actually moved forces a rebuild rather than showing a
    /// stale value. Leaf/composite rows whose value can move at runtime override this to update in place.
    /// </summary>
    protected virtual bool SyncCore(PropertyNode node) => JToken.DeepEquals(node.Value, _value);
}
