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

    [ObservableProperty]
    private decimal? _value;

    public VectorComponentViewModel(string label, JArray array, int index, Action commit)
    {
        Label = label;
        _array = array;
        _index = index;
        _commit = commit;
        _value = PropertyConvert.TryDecimal(array[index]);
    }

    public string Label { get; }

    partial void OnValueChanged(decimal? value)
    {
        if (value is null)
            return;

        _array[_index].Replace(new JValue((double)value.Value));
        _commit();
    }
}
