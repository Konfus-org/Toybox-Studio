using CommunityToolkit.Mvvm.ComponentModel;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// One labeled Euler-angle field (X/Y/Z, in degrees) inside a <see cref="RotationPropertyViewModel"/>.
/// Each edit updates the shared Euler array and asks the owner to recompose the backing quaternion —
/// unlike a plain vector component, no single quaternion element maps to one angle.
/// </summary>
public sealed partial class RotationComponentViewModel : ObservableObject
{
    private readonly double[] _euler;
    private readonly int _index;
    private readonly Action _apply;

    public RotationComponentViewModel(string label, int index, double[] euler, Action apply)
    {
        Label = label;
        _index = index;
        _euler = euler;
        _apply = apply;
        _value = (decimal)euler[index];
    }

    public string Label { get; }

    [ObservableProperty]
    private decimal? _value;

    partial void OnValueChanged(decimal? value)
    {
        if (value is null)
            return;

        _euler[_index] = (double)value.Value;
        _apply();
    }
}
