using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// String property rendered as a text box.
/// </summary>
public sealed partial class StringPropertyViewModel : PropertyViewModelBase
{
    private readonly JToken? _token;

    public StringPropertyViewModel(PropertyNode node) : base(node)
    {
        _token = node.Value;
        _value = node.Value?.Value<string>() ?? "";
    }

    [ObservableProperty]
    private string _value;

    partial void OnValueChanged(string value)
    {
        if (_token is null)
            return;

        _token.Replace(new JValue(value));
        RaiseCommit();
    }
}
