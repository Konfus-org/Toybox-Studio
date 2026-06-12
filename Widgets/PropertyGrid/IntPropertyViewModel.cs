using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;
using Toybox.Studio.Services;

namespace Toybox.Studio.Widgets.PropertyGrid;

/// <summary>
/// Integer-valued property (also used for uuid/handle/enum tokens).
/// </summary>
public sealed partial class IntPropertyViewModel : PropertyViewModelBase
{
    private readonly JToken? _token;

    public IntPropertyViewModel(PropertyNode node) : base(node)
    {
        _token = node.Value;
        _value = PropertyConvert.TryDecimal(node.Value);
    }

    [ObservableProperty]
    private decimal? _value;

    partial void OnValueChanged(decimal? value)
    {
        if (value is null || _token is null)
            return;

        _token.Replace(new JValue((long)value.Value));
        RaiseCommit();
    }
}
