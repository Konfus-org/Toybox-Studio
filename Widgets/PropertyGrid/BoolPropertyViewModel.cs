using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Boolean property rendered as a checkbox.
/// </summary>
public sealed partial class BoolPropertyViewModel : PropertyViewModelBase
{
    private readonly JsonValueSlot _slot;

    public BoolPropertyViewModel(PropertyNode node) : base(node)
    {
        _slot = new JsonValueSlot(node.Value);
        _value = node.Value?.Type == JTokenType.Boolean && node.Value.Value<bool>();
    }

    [ObservableProperty]
    private bool _value;

    partial void OnValueChanged(bool value)
    {
        if (_slot.Set(new JValue(value)))
            RaiseCommit();
    }
}
