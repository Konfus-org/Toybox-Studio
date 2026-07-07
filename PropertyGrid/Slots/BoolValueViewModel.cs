namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>Edits a bool as a check box.</summary>
public sealed class BoolValueViewModel(PropertyValueAccessor accessor) : ValueViewModel(accessor)
{
    public bool Value
    {
        get => Accessor.Get() is true;
        set => Accessor.Set(value);
    }
}
