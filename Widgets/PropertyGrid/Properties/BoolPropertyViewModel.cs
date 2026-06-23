using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Boolean property rendered as a checkbox.
/// </summary>
public sealed partial class BoolPropertyViewModel : PropertyViewModel
{
    private readonly JsonValueSlot _slot;

    [ObservableProperty]
    private bool _value;

    public BoolPropertyViewModel(PropertyNode node) : base(node)
    {
        _slot = new JsonValueSlot(node.Value);
        _value = node.Value?.Type == JTokenType.Boolean && node.Value.Value<bool>();
    }

    public override JToken? CurrentValue => new JValue(Value);

    public override void ApplyValue(JToken token) => Value = token.Type == JTokenType.Boolean && token.Value<bool>();

    protected override bool SyncCore(PropertyNode node)
    {
        Value = node.Value?.Type == JTokenType.Boolean && node.Value.Value<bool>();
        return true;
    }

    partial void OnValueChanged(bool value)
    {
        if (_slot.Set(new JValue(value)))
            RaiseCommit();
    }
}
