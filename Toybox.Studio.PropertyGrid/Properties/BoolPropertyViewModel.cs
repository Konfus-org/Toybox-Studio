using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace Toybox.Studio.PropertyGrid;

/// <summary>
/// Boolean property rendered as a checkbox.
/// </summary>
public sealed partial class BoolPropertyViewModel : PropertyViewModel
{
    [ObservableProperty]
    private bool _value;

    public BoolPropertyViewModel(PropertyDescriptor descriptor, IValueAccessor accessor)
        : base(descriptor, accessor)
    {
        _value = accessor.Get() is true;
    }

    public override void ApplyValue(JToken token) => Value = token.Type == JTokenType.Boolean && token.Value<bool>();

    protected override bool SyncCore(IValueAccessor accessor)
    {
        Value = accessor.Get() is true;
        return true;
    }

    partial void OnValueChanged(bool value)
    {
        Accessor.Set(value);
        RaiseCommit();
    }
}
