using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// String property rendered as a text box.
/// </summary>
public sealed partial class StringPropertyViewModel : PropertyViewModelBase
{
    private readonly JsonValueSlot _slot;

    public StringPropertyViewModel(PropertyNode node) : base(node)
    {
        _slot = new JsonValueSlot(node.Value);
        _value = node.Value?.Value<string>() ?? "";
    }

    [ObservableProperty]
    private string _value;

    partial void OnValueChanged(string value)
    {
        if (_slot.Set(new JValue(value)))
            RaiseCommit();
    }
}
