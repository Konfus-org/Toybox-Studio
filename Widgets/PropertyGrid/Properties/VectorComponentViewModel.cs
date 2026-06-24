using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// One labeled scalar inside a vector (X/Y/Z/W), editing the live array element in place.
/// </summary>
public sealed partial class VectorComponentViewModel : ObservableObject
{
    private readonly JArray _array;
    private readonly int _index;
    private readonly Action _commit;

    // True when this component started as an integer (an integer vector, vec*i): writes must stay integral so
    // the first edit doesn't silently coerce the whole vector to floating point.
    private readonly bool _integer;

    [ObservableProperty]
    private decimal? _value;

    public VectorComponentViewModel(string label, JArray array, int index, Action commit)
    {
        Label = label;
        _array = array;
        _index = index;
        _commit = commit;
        _integer = array[index].Type == JTokenType.Integer;
        _value = PropertyConvert.TryDecimal(array[index]);
    }

    public string Label { get; }

    partial void OnValueChanged(decimal? value)
    {
        if (value is null)
            return;

        // Preserve the element's numeric type: an integer vector stays integral, a float vector stays double.
        var token = _integer ? new JValue((long)value.Value) : new JValue((double)value.Value);
        _array[_index].Replace(token);
        _commit();
    }
}
