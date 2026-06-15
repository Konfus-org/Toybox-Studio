using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Floating-point property (float/double tokens).
/// </summary>
public sealed partial class FloatPropertyViewModel : PropertyViewModel
{
    private readonly JsonValueSlot _slot;

    public FloatPropertyViewModel(PropertyNode node) : base(node)
    {
        _slot = new JsonValueSlot(node.Value);
        _value = PropertyConvert.TryDecimal(node.Value);
    }

    [ObservableProperty]
    private decimal? _value;

    partial void OnValueChanged(decimal? value)
    {
        if (value is null)
            return;

        if (_slot.Set(new JValue((double)value.Value)))
            RaiseCommit();
    }
}
