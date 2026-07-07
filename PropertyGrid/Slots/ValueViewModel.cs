using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// The parent of the view-models that fill a row's value slot. Each concrete editor pairs one value
/// shape with its view (text / number / bool / enum) through a <c>Value</c> property that reads and
/// writes the shared accessor directly — never a cached copy — so a reset (or any other slot's write)
/// is reflected the moment the accessor announces it.
/// </summary>
public abstract class ValueViewModel : ObservableObject
{
    // Every concrete editor names its editable property Value, so one change notification covers them all.
    private const string ValuePropertyName = "Value";

    protected ValueViewModel(PropertyValueAccessor accessor)
    {
        Accessor = accessor;
        accessor.Changed += () => OnPropertyChanged(ValuePropertyName);
    }

    public bool IsReadOnly => Accessor.IsReadOnly;

    protected PropertyValueAccessor Accessor { get; }
}
