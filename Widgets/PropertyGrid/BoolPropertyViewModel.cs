using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Boolean property rendered as a checkbox.
/// </summary>
public sealed partial class BoolPropertyViewModel : PropertyViewModelBase
{
    private readonly JToken? _token;

    public BoolPropertyViewModel(PropertyNode node) : base(node)
    {
        _token = node.Value;
        _value = node.Value?.Type == JTokenType.Boolean && node.Value.Value<bool>();
    }

    [ObservableProperty]
    private bool _value;

    partial void OnValueChanged(bool value)
    {
        if (_token is null)
            return;

        _token.Replace(new JValue(value));
        RaiseCommit();
    }
}
