using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>A list header's "add element" affordance, in its right slot: appends the factory's fresh
/// element to the list.</summary>
public sealed partial class AddItemViewModel(Action add)
{
    [RelayCommand]
    private void Add() => add();
}
