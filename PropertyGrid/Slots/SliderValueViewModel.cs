namespace Toybox.Studio.PropertyGrid.Slots;

/// <summary>
/// Edits a numeric value on a bounded slider (a <c>[Slider(min, max)]</c> property). The slider speaks
/// double; writes convert back to the property's own numeric type, and a value that doesn't fit it (a
/// byte overflow) snaps the editor back instead of committing. The current value renders beside the
/// track so the exact number stays legible while dragging.
/// </summary>
public sealed class SliderValueViewModel : ValueViewModel
{
    private readonly Type _numberType;

    public SliderValueViewModel(
        PropertyValueAccessor accessor, Type numberType, double minimum, double maximum)
        : base(accessor)
    {
        _numberType = numberType;
        Minimum = minimum;
        Maximum = maximum;
    }

    public double Minimum { get; }

    public double Maximum { get; }

    public double Value
    {
        get => Accessor.Get() is { } value ? Convert.ToDouble(value) : Minimum;
        set
        {
            try
            {
                Accessor.Set(Convert.ChangeType(value, _numberType));
            }
            catch (OverflowException)
            {
                OnPropertyChanged(nameof(Value));
            }
        }
    }

    /// <summary>The keyboard/arrow step: whole numbers for an integer property, otherwise a hundredth of
    /// the range so a fractional dial nudges smoothly.</summary>
    public double Step => NumericTypes.IsInteger(_numberType) ? 1 : (Maximum - Minimum) / 100.0;
}
