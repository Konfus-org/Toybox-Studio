using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// A run of properties under one [[tbx::category]] heading. The default (null) group renders header-less
/// at the top; named groups render under a collapsible header. A named group also reads like a composite
/// row: it shows a right-hand state indicator that is "set" exactly when any of its rows is, and resets the
/// whole group (every resettable child to its default) when clicked.
/// </summary>
public sealed class PropertyCategoryGroup : ObservableObject
{
    private bool _visible = true;

    public PropertyCategoryGroup(string? name, IEnumerable<PropertyViewModel> items)
    {
        Name = name;
        Items = new ObservableCollection<PropertyViewModel>(items);
        // No CanExecute gate: the indicator stays full-strength and we suppress the click via IsHitTestVisible
        // (CanReset) instead, matching the per-row StateIndicatorPart so a non-resettable group reads the same.
        ResetCommand = new RelayCommand(ResetChildren);

        // The group's state is the reactive aggregate of its rows — set when any row is set, resettable when
        // any row offers a reset. Track each row so the header indicator follows edits/resets live.
        foreach (var item in Items)
            item.PropertyChanged += OnItemChanged;
    }

    public string? Name { get; }

    public bool HasHeader => !string.IsNullOrEmpty(Name);

    public ObservableCollection<PropertyViewModel> Items { get; }

    /// <summary>Whether the group (header + rows) is shown — false once a filter hides all its rows.</summary>
    public bool Visible
    {
        get => _visible;
        private set => SetProperty(ref _visible, value);
    }

    /// <summary>True when any row in the group differs from its default — drives the header's "set" indicator.</summary>
    public bool IsModified => Items.Any(item => item.IsModified);

    /// <summary>
    /// The group's right-hand indicator state: a filled dot when any row is set, a hollow crater when every
    /// row is at its default. Rendered by <see cref="PropertyStateToIndicatorConverter"/> just like a row.
    /// </summary>
    public PropertyState State => IsModified ? PropertyState.NonDefault : PropertyState.Default;

    /// <summary>True when at least one row offers a reset, so the header indicator becomes a revert affordance.</summary>
    public bool CanReset => Items.Any(item => item.CanReset);

    /// <summary>True only when the group can reset AND currently differs from its default — a default group's
    /// (hollow) indicator is informational, not clickable.</summary>
    public bool IsResettable => CanReset && IsModified;

    /// <summary>Tooltip: a revert hint when resettable, else just "Default".</summary>
    public string Hint => IsResettable ? "Reset to default" : "Default";

    /// <summary>Reverts the whole group: resets every resettable row to its default (rows without a reset are skipped).</summary>
    public ICommand ResetCommand { get; }

    /// <summary>Recomputes group visibility from its rows' current filter state.</summary>
    public void RefreshVisibility() => Visible = Items.Any(item => item.Visible);

    private void OnItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PropertyViewModel.IsModified)
            or nameof(PropertyViewModel.State)
            or nameof(PropertyViewModel.CanReset))
        {
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(CanReset));
            OnPropertyChanged(nameof(IsResettable));
            OnPropertyChanged(nameof(Hint));
        }
    }

    private void ResetChildren()
    {
        foreach (var item in Items)
            item.ResetToDefault?.Invoke();
    }
}
