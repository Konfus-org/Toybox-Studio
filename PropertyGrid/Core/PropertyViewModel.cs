using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Toybox.Studio.EngineApi;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Shared parent for every property widget view-model. A widget is built from a pure-metadata
/// <see cref="PropertyDescriptor"/> paired with a live <see cref="IValueAccessor"/> cursor: leaf widgets read
/// their display value from the accessor, <see cref="IValueAccessor.Set"/> a CLR value on edit, and
/// <see cref="RaiseCommit"/> to persist. A read-only value's accessor reports <see cref="IValueAccessor.CanWrite"/>
/// false and its <see cref="IValueAccessor.Commit"/> is a no-op, so a read-only grid never persists.
/// </summary>
public abstract class PropertyViewModel : ObservableObject
{
    /// <summary>The live value cursor this widget reads and writes; JSON never crosses into a widget.</summary>
    protected IValueAccessor Accessor { get; }

    // Set while a fresh snapshot is being pushed in via <see cref="Sync"/>: tracking engine truth must move
    // the displayed value WITHOUT persisting it straight back as if the user had just typed it.
    private bool _suppressCommit;

    private bool _isModified;

    private bool _visible = true;

    protected PropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor)
    {
        Accessor = accessor;
        Name = string.IsNullOrEmpty(descriptor.Label) ? NameHumanizer.Humanize(descriptor.Name) : descriptor.Label;
        RawName = descriptor.Name;
        Type = descriptor.Type;
        Category = descriptor.Category;
        Description = descriptor.Description;
        IsReadOnly = descriptor.ReadOnly;
        Icon = descriptor.Icon;
        IconColor = descriptor.IconColor;
        // Seed the indicator from the describe flag (accurate for leaves AND composites — a struct/list reports
        // is_default for its whole subtree). Untyped data (settings) has no flag, so this starts false and the
        // host's default-wiring corrects leaves; composites then aggregate from their children.
        _isModified = !descriptor.IsDefault;
        ResetCommand = new RelayCommand(() => ResetToDefault?.Invoke());
        Parts.CollectionChanged += OnPartsChanged;
        // Every row carries a state/reset indicator; composites and list elements append their own parts
        // (the disclosure chevron, the reorder handle, the add/remove affordances) to this same list.
        StateIndicator = new StateIndicatorPart(this);
        Parts.Add(StateIndicator);
    }

    /// <summary>
    /// The always-present state/reset indicator part. Every row gets one in its <see cref="TrailingParts"/>;
    /// this typed handle is for the root composite section headers, which render as an accent band outside the
    /// shared <see cref="PropertyRow"/> chrome and so show the indicator directly rather than via the slot.
    /// </summary>
    public StateIndicatorPart StateIndicator { get; }

    /// <summary>
    /// The composable pieces that make up this row, in declaration order. The shared <see cref="PropertyRow"/>
    /// chrome lays each one out in the slot it declares (<see cref="PropertyPart.Slot"/>), so a row's affordances
    /// are fully dynamic: anything can <c>Parts.Add(...)</c> a new part (a chevron, a handle, add/remove, a
    /// future context menu) without touching this class or the row template. Every row starts with a
    /// <see cref="StateIndicatorPart"/>; a resizable list adds a <see cref="HandlePart"/> +
    /// <see cref="ActionsPart"/> to each element row. The disclosure chevron is the exception — it lives in
    /// <see cref="Disclosure"/> (a reserved gutter), not here, so it never shifts the label.
    /// </summary>
    public ObservableCollection<PropertyPart> Parts { get; } = [];

    /// <summary>
    /// The disclosure (expand/collapse) chevron for a composite row, or null for a leaf. Rendered by the
    /// shared <see cref="PropertyRow"/> chrome in a dedicated fixed-width gutter that is reserved on every
    /// row — present but glyph-less on leaves — so a row's icon and label always start at the same x whether
    /// or not it has children. Composites set this instead of adding the chevron to <see cref="Parts"/>, which
    /// would otherwise shift the label out of alignment with sibling leaf rows.
    /// </summary>
    public DropdownPart? Disclosure { get; protected set; }

    /// <summary>Parts rendered in the indented label gutter, before the icon (the drag-to-reorder handle).</summary>
    public ObservableCollection<PropertyPart> LeadingParts { get; } = [];

    /// <summary>Parts rendered after the value editor, on the right edge (add/remove, state indicator).</summary>
    public ObservableCollection<PropertyPart> TrailingParts { get; } = [];

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
    /// Editor icon for the value's type ([[tbx::icon]]), or <see cref="Icon.None"/>. Badges composite headers.
    /// </summary>
    public Icon Icon { get; }

    /// <summary>
    /// The icon's accent colour, or null.
    /// </summary>
    public Avalonia.Media.Color? IconColor { get; }

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
    /// True for [[editor::readonly]] fields: editable leaf views disable their control, and the value's
    /// accessor withholds the commit (see <see cref="PropertyViewModelFactory"/>).
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// True for composite rows (object/array) that render full-width rather than name+value.
    /// </summary>
    public virtual bool IsComposite => false;

    /// <summary>
    /// Optional explicit commit override. Leaves normally persist through <see cref="Accessor"/>'s own commit
    /// (raised by <see cref="RaiseCommit"/>); a widget with no accessor-carried commit (a hand-wired section
    /// header or a slot editor) can set this to route its commit instead.
    /// </summary>
    public Action? CommitChanges { get; set; }

    /// <summary>
    /// Resets this property to its engine default. Set by the host (inspector) only on rows that support
    /// it — top-level, non-read-only component properties; null everywhere else (settings, nested rows).
    /// </summary>
    public Action? ResetToDefault { get; set; }

    /// <summary>True when a reset affordance should be offered for this row.</summary>
    public bool CanReset => ResetToDefault is not null;

    /// <summary>
    /// When this row is an element of a resizable list, the list it belongs to (set by the list as it wires the
    /// element's reorder/delete affordances); null for any other row. Lets the property context menu offer the
    /// list-item actions (move up/down, duplicate, delete) on top of the value copy/paste every row has.
    /// </summary>
    public ArrayPropertyViewModel? OwningList { get; set; }

    /// <summary>
    /// True when this property's current value differs from its engine default — i.e. it has actually been
    /// set/overridden. Drives the "modified" indicator and revert button. Leaves seed it from the describe
    /// response's <c>is_default</c> flag (at construction and on each <see cref="Sync"/>); composites aggregate
    /// it from their children. Left <c>false</c> wherever the default is unknown (settings grids without the
    /// flag), so a row never shows a false "set" marker.
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
    /// The row's right-hand indicator state, shown for every row (composites included — a struct/list reads as
    /// default when all its children/elements are). Read-only rows show a lock; otherwise a filled circle when
    /// the value differs from its default, a hollow circle when at default (or when the default is unknown).
    /// Rendered by <see cref="PropertyStateToIndicatorConverter"/> via the <see cref="StateIndicator"/> part.
    /// </summary>
    public PropertyState State =>
        IsReadOnly ? PropertyState.ReadOnly
        : IsModified ? PropertyState.NonDefault
        : PropertyState.Default;

    /// <summary>Invokes <see cref="ResetToDefault"/>; available for a reset affordance (e.g. context menu).</summary>
    public ICommand ResetCommand { get; }

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

    // The bare value as text for value-search — the live accessor value serialised to JSON.
    private string ValueText => CurrentValue?.ToString(Formatting.None) ?? "";

    /// <summary>
    /// This row's current value as a bare JSON token — the unit copy/paste and reset operate on. Read live off
    /// the accessor (converted through the shared codec) so it always reflects the current value, whatever the
    /// widget shape. Null only when there genuinely is no value (a header-style row with no accessor value).
    /// </summary>
    public virtual JToken? CurrentValue
    {
        get
        {
            var value = Accessor.Get();
            return value is null ? null : EngineSyncValue.WriteBare(value);
        }
    }

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

    /// <summary>
    /// Sets this row's value from a bare JSON token, going through the same path as a user edit (so it persists
    /// and the bound controls refresh). Used by the reset affordance and by clipboard paste. The default
    /// converts the token to the accessor's CLR type and writes it, then commits; a read-only row is left
    /// unchanged. Widgets that keep a bound display property override this to set that value instead (which
    /// cascades through their change hook to <see cref="IValueAccessor.Set"/> + <see cref="RaiseCommit"/>).
    /// </summary>
    public virtual void ApplyValue(JToken token)
    {
        if (!Accessor.CanWrite)
            return;

        Accessor.Set(EngineSyncValue.ReadBare(Accessor.ValueType, token));
        RaiseCommit();
    }

    /// <summary>
    /// Refreshes this row's displayed value(s) from a fresh snapshot of the same property, WITHOUT persisting
    /// the change back to the engine. Used to track a running game's live values in place, so the grid's
    /// controls (and any in-progress edit) are kept rather than torn down and rebuilt every tick. Returns
    /// false when the new node's shape no longer matches this row (e.g. an array changed length, or a value
    /// of a type this row can't update in place actually changed), telling the host to rebuild instead.
    /// </summary>
    public bool Sync(PropertyDescriptor descriptor, IValueAccessor accessor)
    {
        _suppressCommit = true;
        try
        {
            var synced = SyncCore(accessor);
            // Re-seed the indicator from the fresh describe flag (leaves only; composites aggregate from their
            // children, which re-seed themselves as the subtree syncs). The "modified" dot tracks engine truth
            // from the same describe payload already being fetched — no per-property reflect.isDefault call.
            if (!HasChildren)
                IsModified = !descriptor.IsDefault;
            return synced;
        }
        finally
        {
            _suppressCommit = false;
        }
    }

    protected void RaiseCommit()
    {
        if (_suppressCommit)
            return;

        if (CommitChanges is { } commit)
            commit();
        else
            Accessor.Commit();

        // An edited value IS a set (non-default) value, so mark the row modified optimistically — this is what the
        // host's per-property commit used to do by hand (OnPropertyEdited/CommitSynced). Leaves flip their own
        // indicator here; composites/lists aggregate from their children via the existing PropertyChanged hooks, so
        // they need no direct flip. The authoritative is_default check still runs on the next selection / refresh.
        if (!HasChildren)
            IsModified = true;
    }

    /// <summary>
    /// Type-specific in-place value refresh for <see cref="Sync"/>. The default handles every row that does
    /// not override it conservatively: it reports success only when the value is unchanged from what this row
    /// currently shows, so an untracked type whose value actually moved forces a rebuild rather than showing a
    /// stale value. Leaf/composite rows whose value can move at runtime override this to update in place.
    /// </summary>
    protected virtual bool SyncCore(IValueAccessor accessor)
    {
        var incoming = accessor.Get();
        return JToken.DeepEquals(
            incoming is null ? null : EngineSyncValue.WriteBare(incoming), CurrentValue);
    }

    // Buckets Parts into the two slot collections the row template binds, keeping Parts the single source of
    // truth. Within a bucket parts are ordered by PropertyPart.Order (not insertion order), so the visual
    // layout is stable however the parts happened to be added. Handles plain Add/Remove/Reset.
    private void OnPartsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                foreach (PropertyPart part in e.NewItems)
                    InsertByOrder(Bucket(part), part);
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                foreach (PropertyPart part in e.OldItems)
                    Bucket(part).Remove(part);
                break;
            default:
                LeadingParts.Clear();
                TrailingParts.Clear();
                foreach (var part in Parts)
                    InsertByOrder(Bucket(part), part);
                break;
        }
    }

    private ObservableCollection<PropertyPart> Bucket(PropertyPart part) =>
        part.Slot == PartSlot.Leading ? LeadingParts : TrailingParts;

    // Stable sorted insert: place the part after every existing one with an equal-or-lower Order.
    private static void InsertByOrder(ObservableCollection<PropertyPart> bucket, PropertyPart part)
    {
        var index = 0;
        while (index < bucket.Count && bucket[index].Order <= part.Order)
            index++;
        bucket.Insert(index, part);
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
}
