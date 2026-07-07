using CommunityToolkit.Mvvm.Input;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>A list element row's "delete" affordance, in its right slot: removes the element from the
/// owning list.</summary>
public sealed partial class DeleteItemViewModel(Action delete)
{
    [RelayCommand]
    private void Delete() => delete();
}
