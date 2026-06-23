using System.ComponentModel;
using System.Windows.Input;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// The right-hand state indicator part: a lock for read-only, a filled dot when the value differs from its
/// default, a hollow crater when at default — and, whenever a reset is wired, a click target that reverts the
/// owning row to its default. It simply projects the owner row's <see cref="PropertyViewModel.State"/>,
/// <see cref="PropertyViewModel.CanReset"/> and <see cref="PropertyViewModel.ResetCommand"/>, re-raising as the
/// owner's state changes (the owner raises <c>State</c> when its modified flag moves). Every row carries one;
/// category group bands are not rows, so they get none.
/// </summary>
public sealed class StateIndicatorPart : PropertyPart
{
    private readonly PropertyViewModel _owner;

    // Order 100: the state/reset indicator is pinned to the far right of every row, after any add/remove.
    public StateIndicatorPart(PropertyViewModel owner) : base(PartSlot.Trailing, order: 100)
    {
        _owner = owner;
        _owner.PropertyChanged += OnOwnerChanged;
    }

    public PropertyState State => _owner.State;

    public bool CanReset => _owner.CanReset;

    /// <summary>
    /// True only when the indicator is a live revert affordance: the row supports a reset AND it currently
    /// differs from its default. A row already at its default exposes nothing to reset, so its (hollow)
    /// indicator is purely informational and not clickable.
    /// </summary>
    public bool IsResettable => _owner.CanReset && _owner.State == PropertyState.NonDefault;

    /// <summary>Tooltip: a revert hint when resettable, "Read-only" for locked rows, else just "Default".</summary>
    public string Hint => _owner.State switch
    {
        PropertyState.NonDefault when _owner.CanReset => "Reset to default",
        PropertyState.ReadOnly => "Read-only",
        _ => "Default",
    };

    public ICommand ResetCommand => _owner.ResetCommand;

    private void OnOwnerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PropertyViewModel.State) or nameof(PropertyViewModel.IsModified))
        {
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(CanReset));
            OnPropertyChanged(nameof(IsResettable));
            OnPropertyChanged(nameof(Hint));
        }
    }
}
