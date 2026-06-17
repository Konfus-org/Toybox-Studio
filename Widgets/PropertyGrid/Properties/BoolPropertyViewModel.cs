using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Boolean property rendered as a checkbox.
/// </summary>
public sealed partial class BoolPropertyViewModel : PropertyViewModel
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

    public override JToken? CurrentValue => new JValue(Value);

    public override void ApplyValue(JToken token) => Value = token.Type == JTokenType.Boolean && token.Value<bool>();

    protected override bool SyncCore(PropertyNode node)
    {
        Value = node.Value?.Type == JTokenType.Boolean && node.Value.Value<bool>();
        return true;
    }
}
