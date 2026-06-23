using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services.EngineApi;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// String property rendered as a text box.
/// </summary>
public sealed partial class StringPropertyViewModel : PropertyViewModel
{
    private readonly JsonValueSlot _slot;

    [ObservableProperty]
    private string _value;

    public StringPropertyViewModel(PropertyNode node) : base(node)
    {
        _slot = new JsonValueSlot(node.Value);
        _value = node.Value?.Value<string>() ?? "";
    }

    public override JToken? CurrentValue => new JValue(Value ?? "");

    public override void ApplyValue(JToken token) => Value = token.Value<string>() ?? "";

    protected override bool SyncCore(PropertyNode node)
    {
        Value = node.Value?.Value<string>() ?? "";
        return true;
    }

    partial void OnValueChanged(string value)
    {
        if (_slot.Set(new JValue(value)))
            RaiseCommit();
    }
}
