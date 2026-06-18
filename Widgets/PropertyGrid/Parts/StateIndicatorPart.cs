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

    public StateIndicatorPart(PropertyViewModel owner)
    {
        _owner = owner;
        _owner.PropertyChanged += OnOwnerChanged;
    }

    public PropertyState State => _owner.State;

    public bool CanReset => _owner.CanReset;

    public ICommand ResetCommand => _owner.ResetCommand;

    private void OnOwnerChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PropertyViewModel.State) or nameof(PropertyViewModel.IsModified))
        {
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(CanReset));
        }
    }
}
