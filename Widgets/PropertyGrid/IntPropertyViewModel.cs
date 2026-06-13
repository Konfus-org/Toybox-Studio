using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Integer-valued property (also used for uuid/handle/enum tokens).
/// </summary>
public sealed partial class IntPropertyViewModel : PropertyViewModelBase
{
    private readonly JsonValueSlot _slot;

    public IntPropertyViewModel(PropertyNode node) : base(node)
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

        if (_slot.Set(new JValue((long)value.Value)))
            RaiseCommit();
    }
}
