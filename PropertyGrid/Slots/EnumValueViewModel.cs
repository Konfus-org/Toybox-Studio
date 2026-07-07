namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>Edits an enum as a dropdown over its values.</summary>
public sealed class EnumValueViewModel : ValueViewModel
{
    public EnumValueViewModel(PropertyValueAccessor accessor, Type enumType)
        : base(accessor)
        => Choices = [.. Enum.GetValues(enumType).Cast<object>()];

    public IReadOnlyList<object> Choices { get; }

    public object? Value
    {
        get => Accessor.Get();
        set
        {
            // The dropdown clears its selection transiently while its items refresh; only real picks commit.
            if (value is not null)
                Accessor.Set(value);
        }
    }
}
