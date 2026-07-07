namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// Edits any numeric value in a spinner. The spinner speaks decimal; writes convert back to the
/// property's own numeric type, and a value that doesn't fit it (a byte overflow) snaps the editor
/// back instead of committing.
/// </summary>
public sealed class NumberValueViewModel : ValueViewModel
{
    private readonly Type _numberType;

    public NumberValueViewModel(PropertyValueAccessor accessor, Type numberType)
        : base(accessor)
        => _numberType = numberType;

    public decimal? Value
    {
        get => Accessor.Get() is { } value ? Convert.ToDecimal(value) : null;
        set
        {
            if (value is not { } number)
                return;

            try
            {
                Accessor.Set(Convert.ChangeType(number, _numberType));
            }
            catch (OverflowException)
            {
                OnPropertyChanged(nameof(Value));
            }
        }
    }

    public decimal Increment => IsInteger ? 1m : 0.1m;

    public string Format => IsInteger ? "0" : "0.###";

    private bool IsInteger => NumericTypes.IsInteger(_numberType);
}
